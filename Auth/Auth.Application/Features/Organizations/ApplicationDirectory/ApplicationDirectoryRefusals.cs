using ErrorOr;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// The two refusals of the application directory that are about the bearer token rather than
/// the organization, with the published codes the HTTP answer carries (ADR 0001): the challenge
/// code of a revoked token, and the transport code of a 403. Not catalog errors: no handler
/// elsewhere returns them, and their answers are the bearer contract's (RFC 6750 §3.1), which
/// the endpoint writes.
/// </summary>
public static class ApplicationDirectoryRefusals
{
    /// <summary>
    /// The token's user or application can no longer be served: the user is deleted or may not
    /// renew credentials, or the application is gone or inactive. Answered exactly as a revoked
    /// token, so a caller cannot tell which.
    /// </summary>
    public static readonly Error CallerNotServable = Error.Unauthorized(
        code: "Http.TokenRevoked",
        description: "The access token can no longer be used.");

    /// <summary>
    /// The token was not granted <c>organizations.read</c>, or the application is no longer
    /// allowed it. Answered 403 with <c>error="insufficient_scope"</c>.
    /// </summary>
    public static readonly Error InsufficientScope = Error.Forbidden(
        code: "Http.Forbidden",
        description: "The access token does not carry the scope this endpoint requires.");
}
