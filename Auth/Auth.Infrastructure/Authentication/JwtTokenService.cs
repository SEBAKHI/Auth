using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Infrastructure.Security;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using ErrorOr;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// JWT token service implementation using RS256 (asymmetric) signing.
/// Supports JWKS endpoint for external token validation.
/// </summary>
public class JwtTokenService : IJwtTokenService, IDisposable
{
    private readonly JwtSettings _settings;
    private readonly IOptionsMonitor<JwtSettings>? _liveSettings;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDataProtectionProvider? _dataProtectionProvider;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _securityKey;
    private readonly SigningCredentials _signingCredentials;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private bool _disposed;

    public JwtTokenService(
        IOptions<JwtSettings> settings,
        IPasswordHasher passwordHasher,
        IDataProtectionProvider? dataProtectionProvider = null,
        IOptionsMonitor<JwtSettings>? liveSettings = null)
    {
        _settings = settings.Value;
        _liveSettings = liveSettings;
        _passwordHasher = passwordHasher;
        _dataProtectionProvider = dataProtectionProvider;
        _rsa = LoadOrGenerateKey();
        _securityKey = new RsaSecurityKey(_rsa) { KeyId = _settings.KeyId };
        _signingCredentials = new SigningCredentials(_securityKey, SecurityAlgorithms.RsaSha256);
        _tokenHandler = new JwtSecurityTokenHandler();
    }

    /// <inheritdoc />
    public string GenerateAccessToken(
        User user,
        IEnumerable<string> permissions,
        IEnumerable<string> roles,
        AccessTokenAuthentication authentication,
        Guid? sessionId = null,
        IEnumerable<(Guid OrganizationId, string Code)>? organizationPermissions = null,
        string? audience = null,
        string? scope = null,
        TokenOrganization? organization = null)
    {
        var claims = new List<Claim>
        {
            new(JwtClaimNames.Subject, user.Id.ToString()),
            new(JwtClaimNames.Email, user.Email),
            new(JwtClaimNames.JwtId, Guid.NewGuid().ToString()),
            new(JwtClaimNames.IssuedAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtClaimNames.Name, user.GetFullName()),
            new(JwtClaimNames.GivenName, user.FirstName),
            new(JwtClaimNames.FamilyName, user.LastName),
        };

        // Stable session identifier, constant across access-token refreshes.
        if (sessionId.HasValue)
        {
            claims.Add(new Claim(JwtClaimNames.Sid, sessionId.Value.ToString()));
        }

        // Add preferred language if set
        if (!string.IsNullOrEmpty(user.PreferredLanguage))
        {
            claims.Add(new Claim(JwtClaimNames.Locale, user.PreferredLanguage));
        }

        // Add timezone if set
        if (!string.IsNullOrEmpty(user.TimeZone))
        {
            claims.Add(new Claim(JwtClaimNames.TimeZone, user.TimeZone));
        }

        // Add theme if set
        if (!string.IsNullOrEmpty(user.Theme))
        {
            claims.Add(new Claim(JwtClaimNames.Theme, user.Theme));
        }

        // Add roles as individual claims
        foreach (var role in roles)
        {
            claims.Add(new Claim(JwtClaimNames.Roles, role));
        }

        // Add permissions as individual claims
        foreach (var permission in permissions)
        {
            claims.Add(new Claim(JwtClaimNames.Permissions, permission));
        }

        // Organization-scoped permissions ("{orgId}:{code}") let the gate
        // authorize org endpoints per organization without a database hit.
        if (organizationPermissions != null)
        {
            foreach (var (organizationId, code) in organizationPermissions)
            {
                claims.Add(new Claim(JwtClaimNames.OrgPermissions, $"{organizationId}:{code}"));
            }
        }

        // The one organization this application's delegated grants come from,
        // as two plain strings. Never one without the other.
        if (organization is not null)
        {
            claims.Add(new Claim(JwtClaimNames.OrgId, organization.Id.ToString()));
            claims.Add(new Claim(JwtClaimNames.OrgName, organization.Name));
        }

        // The application's grant, as ONE claim holding the space-delimited
        // string (RFC 9068 §2.2.3). One claim per scope would serialize as a JSON
        // array, which is not what a resource server reading "scope" expects.
        if (!string.IsNullOrEmpty(scope))
        {
            claims.Add(new Claim(JwtClaimNames.Scope, scope));
        }

        // What a platform administrator's session must still prove, only while
        // its platform permissions and roles are withheld.
        if (authentication.Requirement != MfaRequirement.None)
        {
            claims.Add(new Claim(JwtClaimNames.MfaRequirement, authentication.Requirement.ToClaimValue()));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Claims = AuthenticationClaims(authentication),
            // Lifetime is read live so a settings change applies to the next
            // issued token without a restart. Issuer/audience/keys stay on
            // the startup snapshot: validation captured them at boot, and
            // issuing with newer values would break every new token.
            Expires = DateTime.UtcNow.Add((_liveSettings?.CurrentValue ?? _settings).AccessTokenLifetime),
            NotBefore = DateTime.UtcNow,
            IssuedAt = DateTime.UtcNow,
            Issuer = _settings.Issuer,
            // Per-app audience for the authorization-code flow; the platform
            // default for direct first-party logins (console/accounts).
            Audience = string.IsNullOrEmpty(audience) ? _settings.Audience : audience,
            SigningCredentials = _signingCredentials
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        return _tokenHandler.WriteToken(token);
    }

    /// <summary>The <c>typ</c> header of a machine access token (RFC 9068 §2.1).</summary>
    public const string MachineAccessTokenType = "at+jwt";

    /// <inheritdoc />
    public string GenerateMachineAccessToken(string clientId, IReadOnlyCollection<string> scopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        if (scopes.Count == 0)
        {
            throw new ArgumentException("A machine token carries at least one scope.", nameof(scopes));
        }

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            TokenType = MachineAccessTokenType,
            // Issuer and key from the startup snapshot, as for every other access token: the
            // schemes captured them at boot.
            Issuer = _settings.Issuer,
            Audience = MachineClients.Audience,
            IssuedAt = now,
            Expires = now.Add(MachineClients.AccessTokenLifetime),
            Claims = new Dictionary<string, object>
            {
                [JwtClaimNames.Subject] = clientId,
                [JwtClaimNames.ClientId] = clientId,
                // One space-delimited string (RFC 9068 §2.2.3), as on application tokens.
                [JwtClaimNames.Scope] = string.Join(' ', scopes),
                [JwtClaimNames.JwtId] = Guid.NewGuid().ToString(),
            },
            SigningCredentials = _signingCredentials
        };

        // The JSON handler, not _tokenHandler: it writes the typ header from the descriptor, and
        // with default times off it writes exactly iat and exp, and no nbf the claim list does
        // not name.
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    /// <summary>
    /// <c>amr</c> and <c>auth_time</c>, or nothing. Written through the payload
    /// dictionary, not as subject claims: one <see cref="Claim"/> serializes as a
    /// plain string, and <c>amr</c> must be a JSON array even with one value
    /// (OIDC Core §2). The two are emitted together or not at all, so a token never
    /// claims an authentication time for methods it cannot name, or the reverse. An
    /// empty <c>amr</c> (a set no registered value describes) is left out.
    /// </summary>
    private static Dictionary<string, object>? AuthenticationClaims(AccessTokenAuthentication authentication)
    {
        if (!authentication.IsRecorded)
        {
            return null;
        }

        var claims = new Dictionary<string, object>
        {
            [JwtClaimNames.AuthTime] = authentication.AuthTime!.Value.ToUnixTimeSeconds(),
        };

        var amr = authentication.Methods.ToAmrValues();
        if (amr.Count > 0)
        {
            claims[JwtClaimNames.Amr] = amr.ToArray();
        }

        return claims;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Lowercase hex, because these values travel through redirect URLs, cookies and form
    /// bodies, past filters this server does not own. Standard base64 always ended in "=="
    /// (64 bytes leave one byte over), and a client's web application firewall refused the
    /// authorization redirect for it with a 403 on 2026-10-12: percent-encoding does not
    /// help, since the firewall decodes before it matches. base64url still carries "-",
    /// which pairs into "--" (an SQL comment signature) in about 2% of 86-character values,
    /// so a strict firewall would refuse those at random. Hex has no punctuation at all.
    /// <para>
    /// 32 bytes is 256 bits, well past the 2^-160 guessing bound RFC 6749 §10.10 recommends,
    /// and 64 characters is shorter than the 88 of the old format, so anything sized for an
    /// old value holds a new one. Every consumer looks the value up by an HMAC of its exact
    /// text, so values issued before this change stay valid, and so do these after a rollback.
    /// </para>
    /// </remarks>
    public string GenerateRefreshToken()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    }

    /// <inheritdoc />
    public ErrorOr<ClaimsPrincipal> ValidateAccessToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return AuthErrors.InvalidToken;
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudience = _settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _securityKey,
            ValidateLifetime = true,
            ClockSkew = _settings.ClockSkew,
            RequireExpirationTime = true,
            RequireSignedTokens = true
        };

        try
        {
            var principal = _tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.RsaSha256, StringComparison.OrdinalIgnoreCase))
            {
                return AuthErrors.InvalidToken;
            }

            return principal;
        }
        catch (SecurityTokenExpiredException)
        {
            return AuthErrors.TokenExpired;
        }
        catch (SecurityTokenException)
        {
            return AuthErrors.InvalidToken;
        }
        catch (ArgumentException)
        {
            // A string that is not a JWT at all — the header will not base64url
            // decode. IdentityModel raises this as an ArgumentException, which
            // slipped past the SecurityTokenException filter above and escaped
            // the handler, so an anonymous caller posting "aaa.bbb.ccc" to the
            // revocation endpoint was answered 400 carrying IdentityModel's own
            // diagnostic text. Unparseable is simply invalid, and says so.
            return AuthErrors.InvalidToken;
        }
    }

    /// <inheritdoc />
    public string? GetTokenId(string token)
    {
        try
        {
            var jwtToken = _tokenHandler.ReadJwtToken(token);
            return jwtToken.Id;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Guid? GetUserId(string token)
    {
        try
        {
            var jwtToken = _tokenHandler.ReadJwtToken(token);
            var subClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtClaimNames.Subject);

            if (subClaim != null && Guid.TryParse(subClaim.Value, out var userId))
            {
                return userId;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string GetJwks()
    {
        var parameters = _rsa.ExportParameters(false);

        var jwk = new Dictionary<string, object>
        {
            ["kty"] = "RSA",
            ["use"] = "sig",
            ["alg"] = "RS256",
            ["kid"] = _settings.KeyId,
            ["n"] = Base64UrlEncode(parameters.Modulus!),
            ["e"] = Base64UrlEncode(parameters.Exponent!)
        };

        var jwks = new Dictionary<string, object>
        {
            ["keys"] = new[] { jwk }
        };

        return JsonSerializer.Serialize(jwks, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <inheritdoc />
    public string GetPublicKeyPem()
    {
        var publicKey = _rsa.ExportRSAPublicKey();
        var base64 = Convert.ToBase64String(publicKey);
        var pem = new StringBuilder();
        pem.AppendLine("-----BEGIN RSA PUBLIC KEY-----");

        for (var i = 0; i < base64.Length; i += 64)
        {
            pem.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
        }

        pem.AppendLine("-----END RSA PUBLIC KEY-----");
        return pem.ToString();
    }

    /// <inheritdoc />
    public DateTime? GetTokenExpiry(string token)
    {
        try
        {
            var jwtToken = _tokenHandler.ReadJwtToken(token);
            var expiry = jwtToken.ValidTo;
            return expiry == DateTime.MinValue ? null : expiry;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the security key used for token signing and validation.
    /// </summary>
    public SecurityKey GetSecurityKey() => _securityKey;

    private RSA LoadOrGenerateKey()
    {
        var rsa = RSA.Create(2048);

        // Priority 1: Load from file path
        if (!string.IsNullOrEmpty(_settings.PrivateKeyPath) && File.Exists(_settings.PrivateKeyPath))
        {
            var pem = File.ReadAllText(_settings.PrivateKeyPath);
            rsa.ImportFromPem(pem);
            return rsa;
        }

        // Priority 2: Decrypt DPAPI-encrypted key from config (recommended for production)
        if (!string.IsNullOrEmpty(_settings.PrivateKeyEncrypted) && _dataProtectionProvider != null)
        {
            var pem = RsaKeyService.DecryptPrivateKey(_dataProtectionProvider, _settings.PrivateKeyEncrypted);
            rsa.ImportFromPem(pem);
            return rsa;
        }

        // Priority 3: Load plain PEM from config (legacy/development)
        if (!string.IsNullOrEmpty(_settings.PrivateKeyPem))
        {
            rsa.ImportFromPem(_settings.PrivateKeyPem);
            return rsa;
        }

        // Priority 4: Generate new key (development only)
        // In production, this should be configured via one of the above methods
        return rsa;
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _rsa.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
