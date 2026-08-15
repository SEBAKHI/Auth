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

    /// <summary>
    /// Refuses a sign-in whose credentials were correct, because the account is
    /// already at its concurrent-session limit.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="UserErrors.InvalidCredentials"/>. The password
    /// verified, and <c>LoginCommandHandler</c> holds the line that valid
    /// credentials get the recovery path rather than a lie — the same reason
    /// <c>EmailNotConfirmed</c> is its own error at the same point in the flow.
    ///
    /// So the refusal has to carry a way out, or it is a dead end: the operator
    /// may have turned off <c>TerminateOldestOnMax</c>, in which case nothing
    /// frees a slot except signing out elsewhere or waiting for an expiry the
    /// user cannot see. Both numbers and that deadline are quoted here.
    ///
    /// Split in two the way <see cref="UserErrors.AccountLockedUntil"/> is: the
    /// deadline is always known in practice (a refusal implies a live session),
    /// but a code promising one must never render with the slot empty.
    /// </remarks>
    public static Error MaxSessionsReached(
        int activeCount,
        int limit,
        DateTime? earliestExpiry) => Error.Validation(
        code: earliestExpiry.HasValue
            ? "Session.MaxSessionsReachedUntil"
            : "Session.MaxSessionsReached",
        description: earliestExpiry.HasValue
            ? $"You are signed in on {activeCount} of {limit} allowed devices. Sign out on another device to free a slot, or wait until the earliest of these sessions expires at {earliestExpiry.Value:u}."
            : $"You are signed in on {activeCount} of {limit} allowed devices. Sign out on another device to sign in here.",
        metadata: earliestExpiry.HasValue
            ? new() { ["args"] = new object[] { activeCount, limit, earliestExpiry.Value.ToString("u") } }
            : new() { ["args"] = new object[] { activeCount, limit } });
}
