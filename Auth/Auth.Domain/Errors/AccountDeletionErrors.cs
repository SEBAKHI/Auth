using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to the account deletion lifecycle.
/// </summary>
public static class AccountDeletionErrors
{
    /// <summary>
    /// Single error for every OTP failure shape (unknown email, wrong code,
    /// expired code, attempts exhausted) so responses stay indistinguishable
    /// and leak no account state.
    /// </summary>
    public static Error InvalidOtp => Error.Validation(
        code: "AccountDeletion.InvalidOtp",
        description: "The verification code is invalid or has expired.");

    public static Error TooManyRequests => Error.Validation(
        code: "AccountDeletion.TooManyRequests",
        description: "Too many verification codes requested. Please try again later.");

    public static Error NotPendingGrace => Error.Conflict(
        code: "AccountDeletion.NotPendingGrace",
        description: "The deletion request is not pending.");

    public static Error GraceNotElapsed => Error.Conflict(
        code: "AccountDeletion.GraceNotElapsed",
        description: "The grace period has not ended yet.");

    public static Error NotProcessing => Error.Conflict(
        code: "AccountDeletion.NotProcessing",
        description: "The deletion request is not being processed.");

    public static Error ExecutionFailed => Error.Failure(
        code: "AccountDeletion.ExecutionFailed",
        description: "Account deletion execution failed; it will be retried.");

    // Request-validation rules (ADR 0001): validators declare these with
    // WithErrorCode, and the validation behavior carries the offending property.

    public static readonly Error OtpCodeInvalidFormat = Error.Validation(
        code: "AccountDeletion.OtpCodeInvalidFormat",
        description: "Verification code must be 6 digits.");

    public static readonly Error OtpCodeRequired = Error.Validation(
        code: "AccountDeletion.OtpCodeRequired",
        description: "Verification code is required.");
}
