using System.Text.RegularExpressions;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Infrastructure.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// <see cref="IClientAssertionVerifier"/> over the JSON web token handler JwtBearer itself uses
/// (OI-128). Every rule is either a parameter of the handler or a check on the token it validated;
/// nothing is decided from a header or claim the signature does not cover.
/// </summary>
public sealed class ClientAssertionVerifier : IClientAssertionVerifier
{
    /// <summary>
    /// The longest assertion read at all. An ES256 assertion with the claims the rules name is
    /// about 400 characters; the cap keeps an anonymous caller from making the endpoint parse
    /// megabytes of JSON.
    /// </summary>
    public const int MaxAssertionLength = 4096;

    /// <summary>The kid column's width: a thumbprint is 43 characters.</summary>
    public const int MaxKidLength = 64;

    /// <summary>The jti column's width.</summary>
    public const int MaxJtiLength = 200;

    /// <summary>
    /// The <c>typ</c> values accepted when one is sent. draft-ietf-oauth-rfc7523bis recommends
    /// <c>client-authentication+jwt</c> and says servers should not reject an untyped assertion,
    /// so an absent <c>typ</c> passes too; any other value, an access token's <c>at+jwt</c>
    /// above all, is refused.
    /// </summary>
    public static readonly IReadOnlyList<string> AcceptedTypes = ["JWT", "client-authentication+jwt"];

    private static readonly Regex KidShape = new(
        @"^[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private readonly JsonWebTokenHandler _handler = new()
    {
        MapInboundClaims = false,
        MaximumTokenSizeInBytes = MaxAssertionLength
    };

    private readonly TimeProvider _time;

    public ClientAssertionVerifier(TimeProvider time)
    {
        _time = time;
    }

    /// <inheritdoc />
    public string? ReadKeyId(string assertion)
    {
        if (string.IsNullOrEmpty(assertion) || assertion.Length > MaxAssertionLength)
        {
            return null;
        }

        try
        {
            if (!_handler.CanReadToken(assertion))
            {
                return null;
            }

            var token = _handler.ReadJsonWebToken(assertion);
            if (token.IsEncrypted)
            {
                return null;
            }

            var kid = token.Kid;
            // Unverified input from an anonymous caller: only the shape a kid can have (base64url,
            // as an RFC 7638 thumbprint is) goes on to the database lookup and the log.
            return kid is not null && KidShape.IsMatch(kid) ? kid : null;
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException or FormatException)
        {
            // Not a JWT at all: IdentityModel raises malformed input as an ArgumentException.
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ClientAssertionVerification> VerifyAsync(
        string assertion, string publicKeyJwk, ClientAssertionRules rules)
    {
        if (string.IsNullOrEmpty(assertion) || assertion.Length > MaxAssertionLength)
        {
            return ClientAssertionVerification.Refused("assertion missing or too long");
        }

        using var ecdsa = MachineKeyMaterial.PublicKeyFrom(publicKeyJwk);
        var key = new ECDsaSecurityKey(ecdsa)
        {
            // A provider cached across requests would keep this request's ECDsa, which the using
            // above disposes; the next assertion under the same key would fail on a disposed object.
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };

        var parameters = new TokenValidationParameters
        {
            // alg: exactly ES256. "none", every HMAC algorithm (the classic confusion, an HS256 MAC
            // keyed with the public key's bytes) and every RSA one fail here, before any signature
            // is computed.
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            RequireSignedTokens = true,
            IssuerSigningKey = key,

            ValidateIssuer = true,
            ValidIssuer = rules.ClientId,

            // aud: the issuer identifier as the SOLE value, a string or an array of one. The token
            // endpoint's URL is refused, and so is any second audience: an assertion addressed to
            // two servers can be replayed at the other one (the 2025 audience-injection report).
            ValidateAudience = true,
            AudienceValidator = (audiences, _, _) => IsSoleAudience(audiences, rules.Audience),

            // IdentityModel checks exp and nbf against the system clock (its parameters take no
            // TimeProvider); the five-minute and iat checks below read the injected clock, which is
            // the system clock in the API. A test that fakes the clock fakes only the latter.
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = rules.ClockSkew,

            TypeValidator = (type, _, _) => AcceptedType(type),
        };

        var result = await _handler.ValidateTokenAsync(assertion, parameters);
        if (!result.IsValid || result.SecurityToken is not JsonWebToken token)
        {
            return ClientAssertionVerification.Refused(result.Exception?.GetType().Name ?? "invalid");
        }

        if (token.TryGetHeaderValue<object>("crit", out _))
        {
            // RFC 7515 §4.1.11: an extension this server does not understand must be refused.
            return ClientAssertionVerification.Refused("crit header");
        }

        if (!string.Equals(token.Subject, rules.ClientId, StringComparison.Ordinal))
        {
            return ClientAssertionVerification.Refused("sub is not the client");
        }

        var now = _time.GetUtcNow().UtcDateTime;
        if (token.ValidTo > now + MachineClients.MaxAssertionLifetime + rules.ClockSkew)
        {
            return ClientAssertionVerification.Refused("exp too far ahead");
        }

        if (token.TryGetPayloadValue<object>(JwtClaimNames.IssuedAt, out _) && token.IssuedAt > now + rules.ClockSkew)
        {
            return ClientAssertionVerification.Refused("iat in the future");
        }

        var jti = token.Id;
        if (string.IsNullOrWhiteSpace(jti) || jti.Length > MaxJtiLength)
        {
            return ClientAssertionVerification.Refused("jti missing or too long");
        }

        return ClientAssertionVerification.Accepted(jti, DateTime.SpecifyKind(token.ValidTo, DateTimeKind.Utc));
    }

    private static bool IsSoleAudience(IEnumerable<string> audiences, string expected)
    {
        var list = audiences.Take(2).ToList();
        return list.Count == 1 && string.Equals(list[0], expected, StringComparison.Ordinal);
    }

    private static string AcceptedType(string? type)
    {
        if (string.IsNullOrEmpty(type)
            || AcceptedTypes.Any(accepted => string.Equals(accepted, type, StringComparison.OrdinalIgnoreCase)))
        {
            return type ?? string.Empty;
        }

        throw new SecurityTokenInvalidTypeException("The assertion's typ is not accepted.") { InvalidType = type };
    }
}
