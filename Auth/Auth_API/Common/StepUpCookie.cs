using Auth.Application.Configuration;

namespace Auth_API.Common;

/// <summary>
/// Single place that writes, reads and clears the HttpOnly step-up cookie —
/// the server's own memory of having demanded a fresh authentication.
/// </summary>
/// <remarks>
/// Deliberately a cookie rather than a query parameter. The demand used to ride
/// in the authorize URL, where the accounts app deleted it to break the redirect
/// loop; that made "re-authenticate" satisfiable by removing the request for it.
/// A cookie is invisible to page scripts and to anyone editing the address bar,
/// so no client can drop the demand, and a client that mishandles the flow loops
/// instead of silently skipping the check.
/// </remarks>
public static class StepUpCookie
{
    /// <summary>
    /// Records a step-up demand on the response.
    /// </summary>
    public static void Apply(HttpResponse response, string ticket, IdentityProviderSettings settings)
    {
        response.Cookies.Append(settings.StepUpCookieName, ticket, BuildOptions(settings));
    }

    /// <summary>
    /// Reads the step-up ticket from the request cookie, if present.
    /// </summary>
    public static string? Read(HttpRequest request, IdentityProviderSettings settings)
    {
        return request.Cookies.TryGetValue(settings.StepUpCookieName, out var value) &&
               !string.IsNullOrEmpty(value)
            ? value
            : null;
    }

    /// <summary>
    /// Expires the cookie once the demand has been answered, so the same ticket
    /// cannot satisfy a later demand within its lifetime.
    /// </summary>
    public static void Delete(HttpResponse response, IdentityProviderSettings settings)
    {
        response.Cookies.Delete(settings.StepUpCookieName, BuildOptions(settings));
    }

    private static CookieOptions BuildOptions(IdentityProviderSettings settings)
    {
        // Mirrors the IdP session cookie: host-only, and Lax so it rides the
        // top-level navigation back to the authorize endpoint — which is the one
        // request that has to see it — but no cross-site subresource request.
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = settings.StepUpTicketLifetime,
            IsEssential = true
        };
    }
}
