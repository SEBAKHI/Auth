using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Auth.Sdk.Models;
using Auth.Sdk.TokenManagement;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Sdk;

/// <summary>
/// HTTP client for communicating with the AuthSystem API.
/// Includes response caching to minimize remote validation calls
/// and transparent token lifecycle management.
/// </summary>
public class AuthSystemClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ITokenStore _tokenStore;
    private readonly AuthSystemOptions _options;
    private readonly ILogger<AuthSystemClient> _logger;

    public AuthSystemClient(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ITokenStore tokenStore,
        IOptions<AuthSystemOptions> options,
        ILogger<AuthSystemClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _tokenStore = tokenStore;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Validates an API key against the AuthSystem.
    /// Results are cached for the configured duration.
    /// </summary>
    public async Task<ApiKeyValidationResult?> ValidateApiKeyAsync(string rawApiKey, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"apikey:{ComputeCacheKey(rawApiKey)}";

        if (_cache.TryGetValue(cacheKey, out ApiKeyValidationResult? cached))
        {
            return cached;
        }

        try
        {
            var client = CreateClient();
            var response = await client.PostAsJsonAsync(
                "/api/v1/apikeys/validate",
                new { ApiKey = rawApiKey },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("API key validation failed with status {StatusCode}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<ApiKeyValidationResult>(cancellationToken);

            if (result?.Active == true)
            {
                _cache.Set(cacheKey, result, _options.ApiKeyCacheDuration);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate API key against AuthSystem");
            return null;
        }
    }

    /// <summary>
    /// Validates a webhook key against the AuthSystem.
    /// Results are cached for the configured duration.
    /// </summary>
    public async Task<WebhookKeyValidationResult?> ValidateWebhookKeyAsync(string rawWebhookKey, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"webhookkey:{ComputeCacheKey(rawWebhookKey)}";

        if (_cache.TryGetValue(cacheKey, out WebhookKeyValidationResult? cached))
        {
            return cached;
        }

        try
        {
            var client = CreateClient();
            var response = await client.PostAsJsonAsync(
                "/api/v1/webhookkeys/validate",
                new { WebhookKey = rawWebhookKey },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Webhook key validation failed with status {StatusCode}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<WebhookKeyValidationResult>(cancellationToken);

            if (result?.Active == true)
            {
                _cache.Set(cacheKey, result, _options.WebhookKeyCacheDuration);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate webhook key against AuthSystem");
            return null;
        }
    }

    /// <summary>
    /// Introspects a JWT token against the AuthSystem (RFC 7662).
    /// </summary>
    public async Task<TokenIntrospectionResult?> IntrospectTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = CreateClient();
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/introspect",
                new { Token = token },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Token introspection failed with status {StatusCode}", response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<TokenIntrospectionResult>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to introspect token against AuthSystem");
            return null;
        }
    }

    /// <summary>
    /// Authenticates with the AuthSystem and stores tokens for auto-refresh.
    /// Subsequent HTTP requests via the SDK will automatically include and refresh the token.
    /// </summary>
    /// <param name="email">User email address.</param>
    /// <param name="password">User password.</param>
    /// <param name="applicationId">Target application ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if login succeeded, false otherwise.</returns>
    public async Task<bool> LoginAsync(string email, string password, string applicationId, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = CreateClient();
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { email, password, applicationId },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("SDK login failed with status {StatusCode}", response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResult>(cancellationToken);
            if (result?.Token is null)
            {
                return false;
            }

            await _tokenStore.SetAsync(
                result.Token.AccessToken,
                result.Token.RefreshToken,
                result.Token.ExpiresIn,
                cancellationToken);

            _logger.LogDebug("SDK login successful — tokens stored for auto-refresh");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SDK login failed");
            return false;
        }
    }

    /// <summary>
    /// Stores externally obtained tokens for the SDK's auto-refresh mechanism.
    /// Use this when tokens are obtained outside the SDK (e.g., from a frontend login flow).
    /// </summary>
    public async Task SetTokensAsync(string accessToken, string refreshToken, int expiresInSeconds, CancellationToken cancellationToken = default)
    {
        await _tokenStore.SetAsync(accessToken, refreshToken, expiresInSeconds, cancellationToken);
    }

    /// <summary>
    /// Clears stored tokens, effectively logging out the SDK client.
    /// </summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _tokenStore.ClearAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves the named client, already carrying the base address and the
    /// gateway token from its registration.
    /// </summary>
    /// <remarks>
    /// This method used to set both again. IHttpClientFactory runs the
    /// registration delegate for EVERY CreateClient call, so the second
    /// DefaultRequestHeaders.Add appended rather than replaced: X-Gateway-Token
    /// is a custom header with no parser, so it accepts multiple values and went
    /// on the wire as "token, token". The API compares the header as a single
    /// string, so the length check failed before the comparison and every SDK
    /// call through a token-validating API came back 403.
    ///
    /// It survived because Gateway:ValidationEnabled is false in Development and
    /// true in Production, so the defect existed only where nobody was running
    /// it, and the SDK had no tests at all.
    /// </remarks>
    private HttpClient CreateClient() =>
        _httpClientFactory.CreateClient(AuthSystemConstants.HttpClientName);

    /// <summary>
    /// Computes a SHA256 hash of the raw key for use as a cache key.
    /// Never stores the raw key in cache.
    /// </summary>
    private static string ComputeCacheKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToBase64String(bytes);
    }
}
