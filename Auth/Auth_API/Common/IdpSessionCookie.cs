using Auth.Application.Configuration;
using Auth.Application.DTOs;

namespace Auth_API.Common;

/// <summary>
/// Single place that writes, reads and clears the HttpOnly IdP session cookie.
/// The cookie carries the opaque SSO session token minted at interactive login;
/// it never appears in any response body (<see cref="LoginResponse.IdpSessionToken"/>
/// is [JsonIgnore]) and is only readable server-side.
/// </summary>
public static class IdpSessionCookie
{
    /// <summary>
    /// Moves the minted IdP session token from the login response into the
    /// session cookie. No-ops when the response carries no token (2FA pending,
    /// or session creation failed — login still succeeds without SSO).
    /// </summary>
    public static void Apply(HttpResponse response, LoginResponse loginResponse, IdentityProviderSettings settings)
    {
        if (string.IsNullOrEmpty(loginResponse.IdpSessionToken))
        {
            return;
        }

        response.Cookies.Append(
            settings.IdpSessionCookieName,
            loginResponse.IdpSessionToken,
            BuildOptions(settings));
    }

    /// <summary>
    /// Reads the plain IdP session token from the request cookie, if present.
    /// </summary>
    public static string? Read(HttpRequest request, IdentityProviderSettings settings)
    {
        return request.Cookies.TryGetValue(settings.IdpSessionCookieName, out var value) &&
               !string.IsNullOrEmpty(value)
            ? value
            : null;
    }

    /// <summary>
    /// Expires the IdP session cookie (logout).
    /// </summary>
    public static void Delete(HttpResponse response, IdentityProviderSettings settings)
    {
        response.Cookies.Delete(settings.IdpSessionCookieName, BuildOptions(settings));
    }

    private static CookieOptions BuildOptions(IdentityProviderSettings settings)
    {
        // Host-only (no Domain) + Lax: sent on top-level navigations to the
        // authorize endpoint and on same-site XHR from the accounts app, but
        // never on cross-site subresource requests. Never a parent-domain cookie.
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = settings.IdpSessionLifetime,
            IsEssential = true
        };
    }
}
