namespace Auth_API.Common.FirstParty;

/// <summary>Where a refresh credential was read from.</summary>
public enum RefreshCredentialChannel
{
    /// <summary>The request body: script-readable, the channel of non-browser clients.</summary>
    Body,

    /// <summary>The first-party app's HttpOnly refresh cookie.</summary>
    Cookie
}

/// <summary>
/// The refresh token a request presents, and where it came from.
/// </summary>
/// <param name="Value">The plain refresh token.</param>
/// <param name="Channel">The channel it was read from.</param>
/// <param name="App">The first-party app the request's Origin names, if any.</param>
public sealed record RefreshCredential(string Value, RefreshCredentialChannel Channel, FirstPartyApp? App);

/// <summary>
/// One place a refresh request may carry its token. The sources are asked in
/// <see cref="Order"/>; the first that has a credential wins.
/// </summary>
public interface IRefreshCredentialSource
{
    /// <summary>Gets the position in which this source is asked (lower first).</summary>
    int Order { get; }

    /// <summary>
    /// The credential this source finds on the request, or <c>null</c>.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="bodyToken">The body's <c>refreshToken</c>; empty when the body has none.</param>
    /// <param name="app">The first-party app the Origin names, or <c>null</c>.</param>
    RefreshCredential? Read(HttpRequest request, string bodyToken, FirstPartyApp? app);
}

/// <summary>
/// A real token in the body. Asked first: it is how every non-browser client
/// refreshes, and how a session stored by an older app bundle migrates to the
/// cookie on its first refresh. The sentinel is not a token and is ignored.
/// </summary>
public sealed class BodyRefreshCredentialSource(ILogger<BodyRefreshCredentialSource> logger) : IRefreshCredentialSource
{
    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public RefreshCredential? Read(HttpRequest request, string bodyToken, FirstPartyApp? app)
    {
        if (string.IsNullOrEmpty(bodyToken))
        {
            return null;
        }

        if (string.Equals(bodyToken, FirstPartyRefreshCookie.Sentinel, StringComparison.Ordinal))
        {
            // An app bundle from before the cookie echoes what it stored. It still
            // works: the cookie rides along because it always sent credentials.
            logger.LogWarning(
                "SpaRefresh.SentinelEchoed: a refresh body carried the cookie sentinel instead of a token. Origin: {Origin}",
                app?.Origin ?? "(not first-party)");
            return null;
        }

        return new RefreshCredential(bodyToken, RefreshCredentialChannel.Body, app);
    }
}

/// <summary>
/// The first-party app's refresh cookie — read ONLY when the Origin names a listed
/// app. A same-site page that is not first-party (the apex, a sibling subdomain)
/// has the cookie attached by the browser, and must still not be able to spend it.
/// Read whether or not the cookie delivery is switched on, so switching it off
/// returns sessions to the body without signing anyone out.
/// </summary>
public sealed class FirstPartyCookieCredentialSource : IRefreshCredentialSource
{
    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public RefreshCredential? Read(HttpRequest request, string bodyToken, FirstPartyApp? app) =>
        app is not null && FirstPartyRefreshCookie.Read(request, app) is { } token
            ? new RefreshCredential(token, RefreshCredentialChannel.Cookie, app)
            : null;
}

/// <summary>
/// Reads a refresh request's credential through the registered sources, and
/// records it on the request for <see cref="FirstPartySessionResultFilter"/>, which
/// decides how the answer is delivered.
/// </summary>
public sealed class RefreshCredentialReader(
    IEnumerable<IRefreshCredentialSource> sources,
    IFirstPartyOriginResolver originResolver)
{
    private readonly IRefreshCredentialSource[] _sources = sources.OrderBy(source => source.Order).ToArray();

    /// <summary>
    /// The credential the request presents, or <c>null</c> when it presents none.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="bodyToken">The body's <c>refreshToken</c>. An absent member binds as
    /// an empty string, and is treated exactly like one.</param>
    public RefreshCredential? Read(HttpContext httpContext, string? bodyToken)
    {
        var app = originResolver.Resolve(httpContext.Request);
        var credential = _sources
            .Select(source => source.Read(httpContext.Request, bodyToken ?? string.Empty, app))
            .FirstOrDefault(found => found is not null);

        FirstPartySessionContext.SetCredential(httpContext, credential);
        return credential;
    }
}

/// <summary>
/// What a refresh request presented, carried from the action to the result filter.
/// </summary>
public static class FirstPartySessionContext
{
    private static readonly object CredentialKey = new();

    /// <summary>Records the credential the request presented.</summary>
    public static void SetCredential(HttpContext httpContext, RefreshCredential? credential)
    {
        if (credential is not null)
        {
            httpContext.Items[CredentialKey] = credential;
        }
    }

    /// <summary>The credential the request presented, if the action recorded one.</summary>
    public static RefreshCredential? GetCredential(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(CredentialKey, out var value) ? value as RefreshCredential : null;
}
