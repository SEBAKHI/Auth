using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on an expiration date the caller sets.
/// </summary>
public static class ExpiryErrors
{
    public static readonly Error NotInFuture = Error.Validation(
        code: "Expiry.NotInFuture",
        description: "Expiration date must be in the future.");
}
