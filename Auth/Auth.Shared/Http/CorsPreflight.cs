namespace Auth.Shared.Http;

/// <summary>
/// How long a browser may reuse the answer to a CORS preflight, for every host
/// a browser calls from another origin: the gateway and the Auth API.
/// </summary>
/// <remarks>
/// Without <c>Access-Control-Max-Age</c>, Chromium keeps a preflight answer for
/// five seconds, so every page load sent an <c>OPTIONS</c> request ahead of each
/// API call that needs one, and waited for it. On a deployed instance that put one more
/// round trip of 150–570 ms in front of every request a sign-in page makes
/// (measured 2026-10-30). Two hours is Chromium's own ceiling; Firefox allows a
/// day. A reused answer only spares the <c>OPTIONS</c> request: the real
/// response must still carry <c>Access-Control-Allow-Origin</c>, so the page of an
/// origin taken off the list cannot read a response from its next request. The
/// request itself is another matter: for up to two hours, a browser that kept an
/// answer for that address still sends that origin's requests without asking
/// again, and the server runs them. Taking an origin off the list therefore stops
/// its requests completely only after two hours.
/// </remarks>
public static class CorsPreflight
{
    /// <summary>The <c>Access-Control-Max-Age</c> every CORS policy sends.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(2);
}
