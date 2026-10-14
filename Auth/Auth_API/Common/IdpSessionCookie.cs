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
    /// <remarks>
    /// Called only by <see cref="FirstParty.FirstPartySessionResultFilter"/>, the one
    /// place every sign-in exit passes through (FirstPartySignInExitCoverageTests).
    /// </remarks>
    /// <param name="response">The response to write the cookie on.</param>
    /// <param name="idpSessionToken">The plain token of the session just minted.</param>
    /// <param name="persistent">
    /// Whether the person ticked "Stay signed in" (OI-154): the cookie then lasts
    /// the persistent lifetime; otherwise it is a browser-session cookie, with no
    /// Max-Age and no Expires, which the browser drops when it closes.
    /// </param>
    /// <param name="settings">The identity-provider settings.</param>
    public static void Apply(
        HttpResponse response, string? idpSessionToken, bool persistent, IdentityProviderSettings settings)
    {
        if (string.IsNullOrEmpty(idpSessionToken))
        {
            return;
        }

        response.Cookies.Append(
            settings.IdpSessionCookieName,
            idpSessionToken,
            BuildOptions(persistent ? settings.IdpSessionLifetime : null));
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
        response.Cookies.Delete(settings.IdpSessionCookieName, BuildOptions(settings.IdpSessionLifetime));
    }

    private static CookieOptions BuildOptions(TimeSpan? maxAge)
    {
        // Host-only (no Domain) + Lax: sent on top-level navigations to the
        // authorize endpoint and on same-site XHR from the accounts app, but
        // never on cross-site subresource requests. Never a parent-domain cookie.
        // No MaxAge is a browser-session cookie (OI-154).
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = maxAge,
            IsEssential = true
        };
    }
}
