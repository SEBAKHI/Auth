using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Validators.Rules;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Common.Authentication;

/// <summary>
/// The one builder of the API's access-token validation parameters, for both bearer schemes.
/// </summary>
/// <remarks>
/// Two hand-written copies of these parameters drift: the second one is the copy a key rotation
/// or an issuer change misses. So both schemes start from the same base and differ only in the
/// audience rule and the token types; <see cref="BearerSchemeRegistration"/> is the only caller.
/// </remarks>
public static class AccessTokenValidation
{
    /// <summary>
    /// The scheme that accepts an application's access token. Only the userinfo action names it:
    /// a token issued to an application opens that endpoint and nothing else.
    /// </summary>
    public const string UserInfoScheme = "OidcUserInfo";

    /// <summary>
    /// The <c>typ</c> header values an application access token may carry. Today every access
    /// token is minted with <c>JWT</c>; the two RFC 9068 values are accepted ahead of the change
    /// that starts minting them.
    /// </summary>
    public static readonly IReadOnlyList<string> AppAccessTokenTypes = ["JWT", "at+jwt", "application/at+jwt"];

    private static readonly Regex CodeShape = new(
        SharedValidationRules.CodePattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// The platform scheme (the default): tokens issued to the console and the accounts app, whose
    /// audience is <c>Jwt:Audience</c>.
    /// </summary>
    public static TokenValidationParameters Platform(JwtSettings settings, SecurityKey signingKey)
    {
        var parameters = Common(settings, signingKey);
        parameters.ValidAudience = settings.Audience;
        return parameters;
    }

    /// <summary>
    /// The userinfo scheme: the same issuer, key, algorithm and lifetime rules as
    /// <see cref="Platform"/>, but the audience must be exactly one application code, and the
    /// token type one of <see cref="AppAccessTokenTypes"/>.
    /// </summary>
    public static TokenValidationParameters UserInfo(JwtSettings settings, SecurityKey signingKey)
    {
        var parameters = Common(settings, signingKey);
        var platformAudience = settings.Audience;
        parameters.AudienceValidator = (audiences, _, _) => IsApplicationAudience(audiences, platformAudience);
        parameters.ValidTypes = AppAccessTokenTypes;
        return parameters;
    }

    /// <summary>
    /// True when <paramref name="audiences"/> holds exactly one value, shaped like an application
    /// code, that is not the platform audience. A token for two audiences, or for the platform,
    /// is refused: the first could be replayed at another application, the second is a console
    /// token that has <c>/auth/me</c> for this purpose.
    /// </summary>
    private static bool IsApplicationAudience(IEnumerable<string> audiences, string platformAudience)
    {
        var list = audiences.Take(2).ToList();
        if (list.Count != 1)
        {
            return false;
        }

        var audience = list[0];
        return audience.Length <= SharedValidationRules.CodeMaxLength
            && CodeShape.IsMatch(audience)
            && !string.Equals(audience, platformAudience, StringComparison.Ordinal);
    }

    private static TokenValidationParameters Common(JwtSettings settings, SecurityKey signingKey) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = settings.Issuer,
        ValidateAudience = true,
        ValidateLifetime = true,
        ClockSkew = settings.ClockSkew,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        // Pin RS256: never accept a token signed under a different algorithm.
        ValidAlgorithms = ["RS256"],
        IssuerSigningKey = signingKey
    };
}
