using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Auth.Sdk.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Sdk;

/// <summary>
/// HTTP client for communicating with the AuthSystem API.
/// Includes response caching to minimize remote validation calls.
/// </summary>
public class AuthSystemClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly AuthSystemOptions _options;
    private readonly ILogger<AuthSystemClient> _logger;

    public AuthSystemClient(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<AuthSystemOptions> options,
        ILogger<AuthSystemClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
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

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(AuthSystemConstants.HttpClientName);
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.DefaultRequestHeaders.Add(AuthSystemConstants.GatewayTokenHeaderName, _options.GatewayToken);
        return client;
    }

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
