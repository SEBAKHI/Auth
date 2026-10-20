using ErrorOr;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// The refusals of the application directory that carry a published transport or challenge code
/// rather than a catalog one (ADR 0001): the challenge code of a revoked token, the transport code
/// of a 403 for a missing scope (RFC 6750 §3.1), and the transport code of a query value that
/// cannot be read. Not catalog errors: no handler elsewhere returns them, and the first two are
/// answered in the bearer contract's form, which the endpoint writes.
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

    /// <summary>
    /// A <c>status</c> value that is not one exact status name: a 400 with the transport code of a
    /// query value that cannot be read, the answer the framework gives any such value elsewhere.
    /// Decided after the caller, so that a caller that may not read learns that first.
    /// </summary>
    public static readonly Error StatusInvalid = Error.Validation(
        code: "Http.BadRequest",
        description: "A status value is not one of the invitation status names.");
}
