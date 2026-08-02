using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Apple's token lifecycle: exchanges the sign-in authorization code for the
/// refresh token that makes deletion-time revocation possible, and revokes it
/// at Apple when the account is destroyed. Every operation is best-effort by
/// contract — sign-in never breaks over a failed exchange, and the deletion
/// pipeline owns the retry/proceed policy around revocation.
/// </summary>
public class AppleTokenRevocationService : IExternalTokenLifecycle
{
    private const string TokenEndpoint = "https://appleid.apple.com/auth/token";
    private const string RevokeEndpoint = "https://appleid.apple.com/auth/revoke";

    private readonly HttpClient _httpClient;
    private readonly AppleClientSecretGenerator _clientSecretGenerator;
    private readonly IOptionsMonitor<ExternalAuthSettings> _settings;
    private readonly ILogger<AppleTokenRevocationService> _logger;

    public string ProviderName => "apple";

    public AppleTokenRevocationService(
        HttpClient httpClient,
        AppleClientSecretGenerator clientSecretGenerator,
        IOptionsMonitor<ExternalAuthSettings> settings,
        ILogger<AppleTokenRevocationService> logger)
    {
        _httpClient = httpClient;
        _clientSecretGenerator = clientSecretGenerator;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> ExchangeCodeAsync(string authorizationCode, CancellationToken cancellationToken)
    {
        var apple = _settings.CurrentValue.Apple;
        if (apple is null || !apple.Enabled)
        {
            return null;
        }

        try
        {
            using var response = await _httpClient.PostAsync(
                TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = authorizationCode,
                    ["client_id"] = apple.ServicesId,
                    ["client_secret"] = _clientSecretGenerator.Generate()
                }),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Apple authorization-code exchange failed with {StatusCode}; no refresh token will be stored",
                    (int)response.StatusCode);
                return null;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return json.RootElement.TryGetProperty("refresh_token", out var token)
                ? token.GetString()
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Apple authorization-code exchange failed; no refresh token will be stored");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var apple = _settings.CurrentValue.Apple;
        if (apple is null)
        {
            return false;
        }

        try
        {
            using var response = await _httpClient.PostAsync(
                RevokeEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = apple.ServicesId,
                    ["client_secret"] = _clientSecretGenerator.Generate(),
                    ["token"] = refreshToken,
                    ["token_type_hint"] = "refresh_token"
                }),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Apple token revocation failed with {StatusCode}", (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Apple token revocation failed");
            return false;
        }
    }
}
