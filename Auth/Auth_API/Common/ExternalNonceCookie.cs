namespace Auth_API.Common;

/// <summary>
/// Single place that writes and reads the HttpOnly cookie backing a provider
/// sign-in nonce. The cookie holds the HASH of the issued value, never the value
/// itself — the same rule every other opaque token here follows.
/// </summary>
/// <remarks>
/// This cookie is what turns the nonce into evidence. Without it the server
/// compares a value the caller supplied against a token the same caller
/// supplied, which whoever holds a stolen token can satisfy by reading the nonce
/// out of it. With it, the value must be one this server handed to this browser.
/// </remarks>
public static class ExternalNonceCookie
{
    /// <summary>The cookie name. Fixed rather than configurable: it is short-lived and holds no identity.</summary>
    public const string Name = "auth_extnonce";

    /// <summary>
    /// How long an issued nonce stays usable. Long enough to cover a slow
    /// provider sign-in and a password manager, short enough that an abandoned
    /// one does not linger.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Stores the hash of a freshly issued nonce.
    /// </summary>
    public static void Apply(HttpResponse response, string cookieValue)
    {
        response.Cookies.Append(Name, cookieValue, BuildOptions());
    }

    /// <summary>
    /// Reads the stored hash, if the browser holds one.
    /// </summary>
    public static string? Read(HttpRequest request)
    {
        return request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrEmpty(value)
            ? value
            : null;
    }

    private static CookieOptions BuildOptions()
    {
        // Lax, like the other two: the provider sign-in posts from our own origin,
        // and nothing cross-site has any business presenting this.
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = Lifetime,
            IsEssential = true
        };
    }
}
