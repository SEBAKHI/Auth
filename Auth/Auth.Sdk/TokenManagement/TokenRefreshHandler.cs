using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Auth.Sdk.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Sdk.TokenManagement;

/// <summary>
/// HTTP message handler that transparently refreshes expired access tokens.
/// Follows the same pattern as Microsoft MSAL and Auth0 SDKs.
/// </summary>
/// <remarks>
/// A token can be refused before it expires: its signing key was withdrawn, or its session ended.
/// So a 401 is answered by one refresh whatever the token's expiry says (SDK 2.1), once per refused
/// value: concurrent requests refused on the same token share one refresh, a refused token whose
/// refresh produced nothing is not refreshed again for a refusal, and a token that a forced refresh
/// produced is not force-refreshed again until a request with it succeeds (<see cref="ForcedRefreshGuard"/>).
/// </remarks>
public class TokenRefreshHandler : DelegatingHandler
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private static readonly TimeSpan RefreshLockWait = TimeSpan.FromSeconds(10);

    private readonly ITokenStore _tokenStore;
    private readonly AuthSystemOptions _options;
    private readonly ILogger<TokenRefreshHandler> _logger;
    private readonly ForcedRefreshGuard _forcedRefreshes;
    private readonly Func<HttpClient> _refreshClient;

    /// <summary>
    /// For a handler built by hand. The one <c>AddAuthSystemAuthentication</c> registers shares its
    /// forced-refresh guard with every other handler of the container; this one keeps its own.
    /// </summary>
    public TokenRefreshHandler(
        ITokenStore tokenStore,
        IOptions<AuthSystemOptions> options,
        ILogger<TokenRefreshHandler> logger)
        : this(tokenStore, options, logger, new ForcedRefreshGuard())
    {
    }

    internal TokenRefreshHandler(
        ITokenStore tokenStore,
        IOptions<AuthSystemOptions> options,
        ILogger<TokenRefreshHandler> logger,
        ForcedRefreshGuard forcedRefreshes,
        Func<HttpClient>? refreshClient = null)
    {
        _tokenStore = tokenStore;
        _options = options.Value;
        _logger = logger;
        _forcedRefreshes = forcedRefreshes;
        _refreshClient = refreshClient ?? (() => new HttpClient { BaseAddress = new Uri(_options.BaseUrl) });
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableAutoRefresh)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var tokens = await _tokenStore.GetAsync(cancellationToken);
        if (tokens is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // Proactive refresh: if token is near expiry, refresh before sending
        var buffer = TimeSpan.FromSeconds(_options.RefreshBufferSeconds);
        if (DateTimeOffset.UtcNow.Add(buffer) >= tokens.ExpiresAt)
        {
            tokens = await TryRefreshAsync(tokens.RefreshToken, cancellationToken);
            if (tokens is null)
            {
                // Refresh failed — send with current (possibly expired) token
                return await base.SendAsync(request, cancellationToken);
            }
        }

        // Attach the access token
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await base.SendAsync(request, cancellationToken);

        // Reactive refresh: the token this request carried was refused. Replace it once, and retry.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var replacement = await ReplaceRefusedAsync(tokens.AccessToken, cancellationToken);
            if (replacement is null)
            {
                return response;
            }

            // Retry the request with the new token
            response.Dispose();
            var retryRequest = await CloneRequestAsync(request);
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", replacement.AccessToken);
            var retried = await base.SendAsync(retryRequest, cancellationToken);
            if (retried.IsSuccessStatusCode)
            {
                _forcedRefreshes.Succeeded(replacement.AccessToken);
            }

            return retried;
        }

        if (response.IsSuccessStatusCode)
        {
            _forcedRefreshes.Succeeded(tokens.AccessToken);
        }

        return response;
    }

    /// <summary>
    /// The token to retry with after <paramref name="refused"/> was refused, or null when there is
    /// none: the stored token when another request already replaced the refused one, otherwise a
    /// refreshed one, whatever the refused token's expiry says.
    /// </summary>
    private async Task<TokenSet?> ReplaceRefusedAsync(string refused, CancellationToken cancellationToken)
    {
        if (!await RefreshLock.WaitAsync(RefreshLockWait, cancellationToken))
        {
            _logger.LogWarning("Token refresh lock timeout — the refused token is not refreshed");
            var stored = await _tokenStore.GetAsync(cancellationToken);
            return stored is not null && !string.Equals(stored.AccessToken, refused, StringComparison.Ordinal)
                ? stored
                : null;
        }

        try
        {
            var current = await _tokenStore.GetAsync(cancellationToken);
            if (current?.RefreshToken is null)
            {
                return null;
            }

            // Another request was refused on the same token and replaced it while this one waited.
            if (!string.Equals(current.AccessToken, refused, StringComparison.Ordinal))
            {
                return current;
            }

            // A forced refresh produced this token and it was refused too: the refusal is not about
            // its age, and refreshing again would spend a refresh on every request.
            if (_forcedRefreshes.IsUnproven(refused))
            {
                _logger.LogWarning("A refreshed access token was refused again; it is not refreshed until a request with it succeeds");
                return null;
            }

            // Its forced refresh already produced nothing: one per refused value. The refresh at
            // the token's expiry takes over.
            if (_forcedRefreshes.RefreshFailedFor(refused))
            {
                return null;
            }

            var refreshed = await RefreshAsync(current.RefreshToken, cancellationToken);
            if (refreshed is not null)
            {
                _forcedRefreshes.Produced(refreshed.AccessToken);
            }
            else
            {
                _forcedRefreshes.RefreshFailed(refused);
            }

            return refreshed;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private async Task<TokenSet?> TryRefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        // Use semaphore to prevent concurrent refresh calls (thundering herd)
        var acquired = await RefreshLock.WaitAsync(RefreshLockWait, cancellationToken);
        if (!acquired)
        {
            _logger.LogWarning("Token refresh lock timeout — skipping refresh");
            return await _tokenStore.GetAsync(cancellationToken);
        }

        try
        {
            // Double-check: another thread may have already refreshed
            var current = await _tokenStore.GetAsync(cancellationToken);
            var buffer = TimeSpan.FromSeconds(_options.RefreshBufferSeconds);
            if (current is not null && DateTimeOffset.UtcNow.Add(buffer) < current.ExpiresAt)
            {
                return current;
            }

            return await RefreshAsync(refreshToken, cancellationToken);
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    /// <summary>Spends <paramref name="refreshToken"/> for a new pair and stores it. Called only under <see cref="RefreshLock"/>.</summary>
    private async Task<TokenSet?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Refreshing access token...");

            using var httpClient = _refreshClient();
            httpClient.DefaultRequestHeaders.Add(AuthSystemConstants.GatewayTokenHeaderName, _options.GatewayToken);

            var response = await httpClient.PostAsJsonAsync(
                "/api/v1/auth/refresh",
                new { refreshToken },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Token refresh failed with status {StatusCode}", response.StatusCode);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    await _tokenStore.ClearAsync(cancellationToken);
                }
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<TokenRefreshResponse>(cancellationToken);
            if (result is null)
            {
                _logger.LogWarning("Token refresh returned null response");
                return null;
            }

            await _tokenStore.SetAsync(result.AccessToken, result.RefreshToken, result.ExpiresIn, cancellationToken);
            _logger.LogDebug("Access token refreshed successfully");

            return await _tokenStore.GetAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh access token");
            return null;
        }
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        if (original.Content is not null)
        {
            var content = await original.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(content);
            if (original.Content.Headers.ContentType is not null)
            {
                clone.Content.Headers.ContentType = original.Content.Headers.ContentType;
            }
        }

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}
