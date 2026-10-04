using Auth.Shared.Http.ErrorContract;

namespace Auth_API.Common.Errors;

/// <summary>
/// The one way to refuse a bearer token after it was read: by the blacklist, before any action
/// runs, and by userinfo, when the token's subject can no longer be served. Both answers are the
/// same, so a caller cannot tell a revoked token from a deleted or locked account.
/// </summary>
public static class BearerTokenRejection
{
    /// <summary>
    /// A 401 whose reason becomes the problem's code (ADR 0001). No body: the status-code pages
    /// write it. WWW-Authenticate says the bearer token itself was refused (RFC 6750).
    /// </summary>
    public static void Reject(HttpContext context, string reason)
    {
        context.Items[ProblemItems.Code] = reason;
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
    }
}
