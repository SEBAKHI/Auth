using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to session operations.
/// </summary>
public static class SessionErrors
{
    public static Error SessionNotFound => Error.NotFound(
        code: "Session.NotFound",
        description: "The session was not found.");

    public static Error SessionExpired => Error.Validation(
        code: "Session.Expired",
        description: "The session has expired.");

    public static Error SessionAlreadyTerminated => Error.Validation(
        code: "Session.AlreadyTerminated",
        description: "The session has already been terminated.");

    public static Error MaxSessionsReached => Error.Validation(
        code: "Session.MaxSessionsReached",
        description: "Maximum number of concurrent sessions has been reached.");
}
