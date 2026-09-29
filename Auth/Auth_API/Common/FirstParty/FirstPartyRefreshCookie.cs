namespace Auth_API.Common.FirstParty;

/// <summary>
/// Single place that writes, reads and clears a first-party app's refresh cookie.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>__Host-</c> prefix: the browser only accepts it Secure, with Path=/ and
/// no Domain, so a sibling subdomain can neither set nor shadow it.</item>
/// <item>HttpOnly: no page script, and so no XSS or hostile dependency, can read it
/// and carry the session to another machine.</item>
/// <item>SameSite=Strict: it rides on no cross-site request at all. Same-site pages
/// that are not first-party (the apex, a sibling) still get it attached; the Origin
/// check in <see cref="FirstPartyOriginResolver"/> is what stops them.</item>
/// </list>
/// </remarks>
public static class FirstPartyRefreshCookie
{
    /// <summary>
    /// The value that stands in for the refresh token in a response body, and in the
    /// app's storage, when the real token travels in this cookie. The server reads
    /// it back as "no token in the body".
    /// </summary>
    public const string Sentinel = "__cookie__";

    /// <summary>
    /// Stores the refresh token in the app's cookie for its remaining lifetime.
    /// </summary>
    public static void Apply(HttpResponse response, FirstPartyApp app, string refreshToken, int lifetimeSeconds)
    {
        response.Cookies.Append(
            app.RefreshCookieName,
            refreshToken,
            BuildOptions(TimeSpan.FromSeconds(Math.Max(lifetimeSeconds, 0))));
    }

    /// <summary>
    /// Reads the app's refresh token from the request, if present.
    /// </summary>
    public static string? Read(HttpRequest request, FirstPartyApp app) =>
        request.Cookies.TryGetValue(app.RefreshCookieName, out var value) && !string.IsNullOrEmpty(value)
            ? value
            : null;

    /// <summary>
    /// Expires the app's refresh cookie (<c>Max-Age=0</c>).
    /// </summary>
    public static void Delete(HttpResponse response, FirstPartyApp app)
    {
        // Append rather than Cookies.Delete: Delete writes an Expires date, and the
        // explicit Max-Age=0 is the unambiguous form every browser honours at once.
        response.Cookies.Append(app.RefreshCookieName, string.Empty, BuildOptions(TimeSpan.Zero));
    }

    private static CookieOptions BuildOptions(TimeSpan maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        MaxAge = maxAge,
        IsEssential = true
    };
}
