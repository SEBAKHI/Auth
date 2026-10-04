using System.Security.Claims;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Infrastructure.Authentication;
using Auth_API.Common.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.Authentication.OidcUserInfo;

/// <summary>
/// Real access tokens for the userinfo tests: minted by a real <see cref="JwtTokenService"/>
/// with a throwaway in-memory key, and, for the shapes that service never mints (two
/// audiences, another algorithm, an expired or foreign token), by a
/// <see cref="JsonWebTokenHandler"/> signing with the same key. Validated by the handler type
/// JwtBearer itself uses, so a parameter that passes here passes on the wire.
/// </summary>
public sealed class UserInfoTokens : IDisposable
{
    public const string Issuer = "https://auth.example.com";

    /// <summary>A code-shaped platform audience, so only the ordinal check can refuse it.</summary>
    public const string CodeShapedPlatformAudience = "authsystem-api";

    /// <summary>The shape the production placeholder (JWT_AUDIENCE_URL) asks for.</summary>
    public const string UrlPlatformAudience = "https://auth.example.com/api";

    public const string Application = "edis";

    public UserInfoTokens(string platformAudience = CodeShapedPlatformAudience)
    {
        Settings = new JwtSettings
        {
            Issuer = Issuer,
            Audience = platformAudience,
            KeyId = "test-key"
        };
        Service = new JwtTokenService(Options.Create(Settings), Mock.Of<IPasswordHasher>());
    }

    public JwtSettings Settings { get; }

    public JwtTokenService Service { get; }

    public SecurityKey Key => Service.GetSecurityKey();

    public TokenValidationParameters UserInfoParameters => AccessTokenValidation.UserInfo(Settings, Key);

    public TokenValidationParameters PlatformParameters => AccessTokenValidation.Platform(Settings, Key);

    /// <summary>An application's access token, as the code exchange mints it.</summary>
    public string ForApplication(User user, string? scope, string audience = Application, Guid? sessionId = null) =>
        Service.GenerateAccessToken(user, [], [], sessionId, organizationPermissions: null, audience, scope);

    /// <summary>A console or accounts token: the platform audience, roles and permissions, no scope.</summary>
    public string ForPlatform(User user) =>
        Service.GenerateAccessToken(user, ["users:read"], ["Admin"], sessionId: Guid.NewGuid());

    /// <summary>
    /// A token the service never mints, signed with the same key: <paramref name="customize"/>
    /// changes the descriptor of an otherwise valid application token.
    /// </summary>
    public string Custom(Guid subject, Action<SecurityTokenDescriptor> customize)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Application,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(15),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject.ToString(),
                ["jti"] = Guid.NewGuid().ToString(),
                ["scope"] = "openid"
            },
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256)
        };
        customize(descriptor);

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public static async Task<TokenValidationResult> ValidateAsync(string token, TokenValidationParameters parameters) =>
        await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(token, parameters);

    /// <summary>The principal the userinfo scheme builds from <paramref name="token"/>.</summary>
    public async Task<ClaimsPrincipal> UserInfoPrincipalAsync(string token)
    {
        var result = await ValidateAsync(token, UserInfoParameters);
        result.IsValid.Should().BeTrue("the token must pass the userinfo scheme: {0}", result.Exception?.Message);
        return new ClaimsPrincipal(result.ClaimsIdentity);
    }

    public void Dispose() => Service.Dispose();
}
