using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Google external authentication provider.
/// Validates Google ID tokens using Google's public keys via the Google.Apis.Auth library.
/// </summary>
public class GoogleAuthProvider : IExternalAuthProvider
{
    private readonly IOptionsMonitor<ExternalAuthSettings> _settings;
    private readonly ILogger<GoogleAuthProvider> _logger;

    public string ProviderName => "google";

    public GoogleAuthProvider(
        IOptionsMonitor<ExternalAuthSettings> settings,
        ILogger<GoogleAuthProvider> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<ExternalUserInfo>> ValidateTokenAsync(
        string idToken,
        string? nonce,
        CancellationToken cancellationToken)
    {
        var googleSettings = _settings.CurrentValue.Google;
        if (googleSettings == null || !googleSettings.Enabled || string.IsNullOrEmpty(googleSettings.ClientId))
        {
            return ExternalAuthErrors.ProviderNotConfigured("google");
        }

        try
        {
            var validationSettings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { googleSettings.ClientId }
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, validationSettings);

            // Nonce validation for token replay prevention
            if (nonce != null)
            {
                var payloadNonce = ExtractNonceFromJwt(idToken);
                if (payloadNonce != nonce)
                {
                    _logger.LogWarning("Google ID token nonce mismatch");
                    return ExternalAuthErrors.TokenVerificationFailed;
                }
            }

            var firstName = payload.GivenName ?? payload.Name?.Split(' ').FirstOrDefault() ?? "";
            var lastName = payload.FamilyName ?? payload.Name?.Split(' ').LastOrDefault() ?? "";

            return new ExternalUserInfo(
                ProviderUserId: payload.Subject,
                Email: payload.Email,
                FirstName: firstName,
                LastName: lastName,
                DisplayName: payload.Name,
                PictureUrl: payload.Picture,
                EmailVerified: payload.EmailVerified);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Google ID token validation failed");
            return ExternalAuthErrors.TokenVerificationFailed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Google ID token validation");
            return ExternalAuthErrors.TokenVerificationFailed;
        }
    }

    /// <summary>
    /// Extracts the nonce claim from the JWT payload.
    /// GoogleJsonWebSignature.Payload doesn't expose custom claims directly,
    /// so we decode the Base64Url-encoded payload section of the JWT.
    /// </summary>
    private static string? ExtractNonceFromJwt(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;

        var base64 = parts[1].Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        var payloadBytes = Convert.FromBase64String(base64);
        using var doc = JsonDocument.Parse(payloadBytes);
        return doc.RootElement.TryGetProperty("nonce", out var nonceElement)
            ? nonceElement.GetString()
            : null;
    }
}
