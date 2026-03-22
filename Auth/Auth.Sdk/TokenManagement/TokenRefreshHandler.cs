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
public class TokenRefreshHandler : DelegatingHandler
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private readonly ITokenStore _tokenStore;
    private readonly AuthSystemOptions _options;
    private readonly ILogger<TokenRefreshHandler> _logger;

    public TokenRefreshHandler(
        ITokenStore tokenStore,
        IOptions<AuthSystemOptions> options,
        ILogger<TokenRefreshHandler> logger)
    {
        _tokenStore = tokenStore;
        _options = options.Value;
        _logger = logger;
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

        // Reactive refresh: if 401, try refresh once and retry
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokens = await _tokenStore.GetAsync(cancellationToken);
            if (tokens?.RefreshToken is null)
            {
                return response;
            }

            var refreshedTokens = await TryRefreshAsync(tokens.RefreshToken, cancellationToken);
            if (refreshedTokens is null)
            {
                return response;
            }

            // Retry the request with the new token
            response.Dispose();
            var retryRequest = await CloneRequestAsync(request);
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshedTokens.AccessToken);
            return await base.SendAsync(retryRequest, cancellationToken);
        }

        return response;
    }

    private async Task<TokenSet?> TryRefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        // Use semaphore to prevent concurrent refresh calls (thundering herd)
        var acquired = await RefreshLock.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
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

            _logger.LogDebug("Refreshing access token...");

            using var httpClient = new HttpClient { BaseAddress = new Uri(_options.BaseUrl) };
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
        finally
        {
            RefreshLock.Release();
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
