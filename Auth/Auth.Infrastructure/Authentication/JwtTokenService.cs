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
using Auth.Domain.Errors;
using ErrorOr;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
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
        Guid? sessionId = null,
        IEnumerable<(Guid OrganizationId, string Code)>? organizationPermissions = null,
        string? audience = null)
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

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
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

    /// <inheritdoc />
    public string GenerateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
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
