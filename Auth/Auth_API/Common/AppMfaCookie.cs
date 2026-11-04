using Auth.Infrastructure.Authentication;

namespace Auth_API.Common;

/// <summary>
/// Single place that writes, reads and clears the application two-step cookie (S23): the
/// server's signed record of having sent a person to sign in for an application's two-step
/// requirement, which lets the authorize endpoint end a loop instead of redirecting again.
/// </summary>
/// <remarks>
/// A <c>__Host-</c> cookie (ARR-0802): Secure, <c>Path=/</c> and no <c>Domain</c>, so a page on
/// a sibling subdomain cannot plant one. Lax, like the step-up cookie beside it, so it rides
/// the top-level navigation back to the authorize endpoint. It never replaces or reads the
/// step-up cookie.
/// </remarks>
public static class AppMfaCookie
{
    /// <summary>The cookie's name. The prefix is a browser rule, not decoration: keep it.</summary>
    public const string Name = "__Host-auth_appmfa";

    /// <summary>Records the ticket on the response.</summary>
    public static void Apply(HttpResponse response, string ticket)
    {
        response.Cookies.Append(Name, ticket, BuildOptions());
    }

    /// <summary>Reads the ticket from the request cookie, if present.</summary>
    public static string? Read(HttpRequest request)
    {
        return request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrEmpty(value)
            ? value
            : null;
    }

    /// <summary>Expires the cookie: the request that raised its ticket is over.</summary>
    public static void Delete(HttpResponse response)
    {
        response.Cookies.Delete(Name, BuildOptions());
    }

    private static CookieOptions BuildOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = AppMfaTicketService.Lifetime,
            IsEssential = true
        };
    }
}
