using System.IdentityModel.Tokens.Jwt;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Apple ("Sign in with Apple") external authentication provider. Validates
/// Apple ID tokens against Apple's JWKS (signature, issuer, audience = the
/// Services ID, lifetime, nonce). Apple's ID token never carries the user's
/// name — the first-authorization name arrives client-side and flows through
/// the command instead — and the email may be a private-relay address, which
/// is treated like any other.
/// </summary>
public class AppleAuthProvider : IExternalAuthProvider
{
    public const string AppleIssuer = "https://appleid.apple.com";
    private const string JwksEndpoint = "https://appleid.apple.com/auth/keys";
    private static readonly TimeSpan JwksCacheTtl = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<ExternalAuthSettings> _settings;
    private readonly ILogger<AppleAuthProvider> _logger;

    // Benign race: concurrent refreshes fetch the same document twice at worst.
    private JsonWebKeySet? _cachedJwks;
    private DateTime _jwksFetchedAtUtc;

    public string ProviderName => "apple";

    public AppleAuthProvider(
        HttpClient httpClient,
        IOptionsMonitor<ExternalAuthSettings> settings,
        ILogger<AppleAuthProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<ExternalUserInfo>> ValidateTokenAsync(
        string idToken,
        string? nonce,
        CancellationToken cancellationToken)
    {
        var apple = _settings.CurrentValue.Apple;
        if (apple == null || !apple.Enabled || string.IsNullOrEmpty(apple.ServicesId))
        {
            return ExternalAuthErrors.ProviderNotConfigured("apple");
        }

        try
        {
            var jwks = await GetJwksAsync(cancellationToken);

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(
                idToken,
                new TokenValidationParameters
                {
                    ValidIssuer = AppleIssuer,
                    ValidAudience = apple.ServicesId,
                    IssuerSigningKeys = jwks.Keys,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true
                },
                out var validatedToken);

            var jwt = (JwtSecurityToken)validatedToken;

            // Nonce validation for token replay prevention.
            //
            // Driven by the TOKEN, not by the caller — see the same reasoning
            // spelled out in GoogleAuthProvider. Comparing only when the caller
            // supplied a value let a replayer strip the field and have the check
            // skipped entirely. A token carrying no nonce claim is unaffected.
            var payloadNonce = jwt.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
            if (!ExternalNonceComparison.IsSatisfied(payloadNonce, nonce))
            {
                _logger.LogWarning(
                    "Apple ID token nonce mismatch (token carried a nonce: {TokenHasNonce}, caller presented one: {CallerHasNonce})",
                    !string.IsNullOrEmpty(payloadNonce), !string.IsNullOrEmpty(nonce));
                return ExternalAuthErrors.TokenVerificationFailed;
            }

            var email = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email)?.Value;
            if (string.IsNullOrEmpty(jwt.Subject) || string.IsNullOrEmpty(email))
            {
                _logger.LogWarning("Apple ID token is missing the subject or email claim");
                return ExternalAuthErrors.TokenVerificationFailed;
            }

            // Apple emits email_verified as the string "true"/"false" (or a
            // boolean); treat anything but an explicit true as unverified.
            var emailVerified = string.Equals(
                jwt.Claims.FirstOrDefault(c => c.Type == "email_verified")?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase);

            return new ExternalUserInfo(
                ProviderUserId: jwt.Subject,
                Email: email,
                FirstName: "",
                LastName: "",
                DisplayName: null,
                PictureUrl: null,
                EmailVerified: emailVerified);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Apple ID token validation failed");
            return ExternalAuthErrors.TokenVerificationFailed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Apple ID token validation");
            return ExternalAuthErrors.TokenVerificationFailed;
        }
    }

    private async Task<JsonWebKeySet> GetJwksAsync(CancellationToken cancellationToken)
    {
        var cached = _cachedJwks;
        if (cached is not null && DateTime.UtcNow - _jwksFetchedAtUtc < JwksCacheTtl)
        {
            return cached;
        }

        var json = await _httpClient.GetStringAsync(JwksEndpoint, cancellationToken);
        var jwks = new JsonWebKeySet(json);
        _cachedJwks = jwks;
        _jwksFetchedAtUtc = DateTime.UtcNow;
        return jwks;
    }
}
