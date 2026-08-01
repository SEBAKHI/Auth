using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Auth.Application.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Generates the short-lived ES256 client secret Apple requires on its token
/// and revocation endpoints: a JWT signed with the .p8 key (issuer = Team ID,
/// subject = Services ID, audience = Apple). A fresh secret is minted per
/// call — Apple allows up to 6 months of validity, but 5 minutes covers a
/// single request/retry window with no reuse surface.
/// </summary>
public class AppleClientSecretGenerator
{
    private const string AppleAudience = "https://appleid.apple.com";
    private static readonly TimeSpan SecretLifetime = TimeSpan.FromMinutes(5);

    private readonly IOptionsMonitor<ExternalAuthSettings> _settings;

    public AppleClientSecretGenerator(IOptionsMonitor<ExternalAuthSettings> settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Mints a signed client secret for Apple's OAuth endpoints.
    /// </summary>
    /// <exception cref="InvalidOperationException">When Apple is not fully configured.</exception>
    public string Generate()
    {
        var apple = _settings.CurrentValue.Apple;
        if (apple is null
            || string.IsNullOrWhiteSpace(apple.TeamId)
            || string.IsNullOrWhiteSpace(apple.ServicesId)
            || string.IsNullOrWhiteSpace(apple.KeyId)
            || string.IsNullOrWhiteSpace(apple.PrivateKeyPem))
        {
            throw new InvalidOperationException(
                "Apple Sign-In is not fully configured: ExternalAuth:Apple requires TeamId, ServicesId, KeyId " +
                "and the .p8 PrivateKeyPem (provisioned via SecretManagement as AppleSigningKeyPem).");
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(apple.PrivateKeyPem);

        var signingKey = new ECDsaSecurityKey(ecdsa) { KeyId = apple.KeyId };
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: apple.TeamId,
            audience: AppleAudience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, apple.ServicesId)],
            notBefore: now,
            expires: now.Add(SecretLifetime),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
