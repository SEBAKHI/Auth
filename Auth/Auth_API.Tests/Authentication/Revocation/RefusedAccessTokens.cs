using System.Security.Cryptography;
using System.Text;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.Authentication.Revocation;

/// <summary>
/// Access tokens both bearer schemes refuse, each one plausible on its face: signed with the
/// server's own key under a forbidden algorithm, aimed at two audiences, or not signed at all.
/// The revocation endpoint is anonymous, so it must store nothing for any of them.
/// </summary>
public static class RefusedAccessTokens
{
    private static readonly Dictionary<string, Func<UserInfoTokens, Guid, string>> Factories = new()
    {
        ["forged signature"] = (tokens, subject) =>
        {
            // A real signature over another payload: the classic edit-the-claims forgery.
            var genuine = tokens.Custom(subject, _ => { }).Split('.');
            var other = tokens.Custom(Guid.NewGuid(), _ => { }).Split('.');
            return $"{genuine[0]}.{other[1]}.{genuine[2]}";
        },
        ["RS512 under the server's key"] = (tokens, subject) => tokens.Custom(subject, descriptor =>
            descriptor.SigningCredentials = new SigningCredentials(tokens.Key, SecurityAlgorithms.RsaSha512)),
        ["HS256 keyed with the server's public key"] = (tokens, subject) => tokens.Custom(subject, descriptor =>
            descriptor.SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(((RsaSecurityKey)tokens.Key).Rsa.ExportSubjectPublicKeyInfo()),
                SecurityAlgorithms.HmacSha256)),
        ["alg none"] = (_, subject) => Unsigned(subject),
        ["a key of the same kid that is not the server's"] = (tokens, subject) => tokens.Custom(subject, descriptor =>
            descriptor.SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(RSA.Create(2048)) { KeyId = tokens.Key.KeyId }, SecurityAlgorithms.RsaSha256)),
        ["two audiences"] = (tokens, subject) => tokens.Custom(subject, descriptor =>
        {
            descriptor.Audience = null;
            descriptor.Claims["aud"] = new List<string> { UserInfoTokens.Application, "other-app" };
        }),
        ["another issuer"] = (tokens, subject) => tokens.Custom(subject, descriptor =>
            descriptor.Issuer = "https://evil.example.com"),
        ["an id-token type"] = (tokens, subject) => tokens.Custom(subject, descriptor =>
            descriptor.TokenType = "id+jwt"),
        ["expired"] = (tokens, subject) => tokens.Custom(subject, descriptor =>
        {
            descriptor.IssuedAt = DateTime.UtcNow.AddMinutes(-30);
            descriptor.NotBefore = DateTime.UtcNow.AddMinutes(-30);
            descriptor.Expires = DateTime.UtcNow.AddMinutes(-10);
        }),
        ["not a JWT"] = (_, _) => "not-a-jwt",
        ["three segments of junk"] = (_, _) => "aaa.bbb.ccc",
        ["over the parser's size limit"] = (tokens, subject) =>
            tokens.Custom(subject, descriptor => descriptor.Claims["pad"] = new string('a', 300_000)),
    };

    /// <summary>Every case by name, for <c>[MemberData]</c>.</summary>
    public static TheoryData<string> Cases
    {
        get
        {
            var cases = new TheoryData<string>();
            foreach (var name in Factories.Keys)
            {
                cases.Add(name);
            }

            return cases;
        }
    }

    /// <summary>The token named <paramref name="name"/>, for <paramref name="subject"/>.</summary>
    public static string Build(string name, UserInfoTokens tokens, Guid subject) => Factories[name](tokens, subject);

    /// <summary>An application token with <c>alg: none</c> and an empty signature.</summary>
    private static string Unsigned(Guid subject)
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64Url("""{"alg":"none","typ":"JWT"}""");
        var payload = Base64Url(
            $$"""{"iss":"{{UserInfoTokens.Issuer}}","aud":"{{UserInfoTokens.Application}}","sub":"{{subject}}","jti":"{{Guid.NewGuid()}}","iat":{{now.ToUnixTimeSeconds()}},"nbf":{{now.ToUnixTimeSeconds()}},"exp":{{now.AddMinutes(15).ToUnixTimeSeconds()}}}""");
        return $"{header}.{payload}.";
    }

    private static string Base64Url(string json) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));
}
