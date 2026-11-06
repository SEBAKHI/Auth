using Auth.Domain.Entities;
using Microsoft.Net.Http.Headers;

namespace Auth_API.Common.Passkeys;

/// <summary>
/// Single place that writes, reads and clears the passkey ceremony cookie (S20, PK6,
/// ARR-0687): the random handle of the challenge the server holds for five minutes.
/// </summary>
/// <remarks>
/// <para>
/// A <c>__Host-</c> cookie: Secure, <c>Path=/</c> and no <c>Domain</c>, so a page on a sibling
/// host of the same site cannot plant one (cookie tossing); Strict, because only the
/// platform's own pages call the passkey endpoints, from script; HttpOnly, so no script reads
/// the handle. Path is <c>/</c> because the <c>__Host-</c> prefix requires it.
/// </para>
/// <para>
/// A request that carries the cookie more than once is refused by the handler, and spends
/// nothing: one of the two may be a plant.
/// </para>
/// </remarks>
public static class WebAuthnChallengeCookie
{
    /// <summary>The cookie's name. The prefix is a browser rule, not decoration: keep it.</summary>
    public const string Name = "__Host-auth_webauthn";

    /// <summary>Records the handle on the response.</summary>
    public static void Apply(HttpResponse response, string handle)
    {
        response.Cookies.Append(Name, handle, BuildOptions());
    }

    /// <summary>
    /// Reads the handle, and whether the request carried the cookie more than once, from the
    /// raw <c>Cookie</c> header: the parsed collection keeps one value of a repeated name and
    /// would hide the second.
    /// </summary>
    public static (string? Handle, bool Repeated) Read(HttpRequest request)
    {
        var values = new List<string>();
        foreach (var header in request.Headers.Cookie)
        {
            if (header is not null && CookieHeaderValue.TryParseList([header], out var cookies))
            {
                values.AddRange(cookies
                    .Where(cookie => string.Equals(cookie.Name.Value, Name, StringComparison.Ordinal))
                    .Select(cookie => cookie.Value.Value ?? string.Empty));
            }
        }

        return values.Count switch
        {
            0 => (null, false),
            1 => (string.IsNullOrEmpty(values[0]) ? null : values[0], false),
            _ => (null, true),
        };
    }

    /// <summary>Expires the cookie: the challenge it named was spent.</summary>
    public static void Delete(HttpResponse response)
    {
        response.Cookies.Delete(Name, BuildOptions());
    }

    internal static CookieOptions BuildOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
        Path = "/",
        MaxAge = WebAuthnChallenge.Lifetime,
        IsEssential = true,
    };
}
