using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to two-factor authentication operations.
/// </summary>
public static class TwoFactorErrors
{
    public static Error SetupRequired => Error.Validation(
        code: "TwoFactor.SetupRequired",
        description: "Two-factor authentication setup is required before enabling.");

    public static Error VerificationRequired => Error.Unauthorized(
        code: "TwoFactor.VerificationRequired",
        description: "Two-factor authentication verification is required.");

    public static Error ChallengeInvalid => Error.Unauthorized(
        code: "TwoFactor.ChallengeInvalid",
        description: "The two-factor challenge is invalid or has expired. Please sign in again.");

    public static Error LockedOut => Error.Forbidden(
        code: "TwoFactor.LockedOut",
        description: "Two-factor authentication has been temporarily locked due to too many failed attempts.");

    public static Error InvalidRecoveryCode => Error.Validation(
        code: "TwoFactor.InvalidRecoveryCode",
        description: "The recovery code is invalid.");

    public static Error NoRecoveryCodesAvailable => Error.Validation(
        code: "TwoFactor.NoRecoveryCodesAvailable",
        description: "No recovery codes are available. Please contact support.");

    // Request-validation rules (ADR 0001): validators declare these with
    // WithErrorCode, and the validation behavior carries the offending property.

    public static readonly Error ChallengeTokenRequired = Error.Validation(
        code: "TwoFactor.ChallengeTokenRequired",
        description: "The two-factor challenge token is required.");

    public static readonly Error CodeInvalidFormat = Error.Validation(
        code: "TwoFactor.CodeInvalidFormat",
        description: "Verification code must be a 6-digit number.");

    public static readonly Error CodeRequired = Error.Validation(
        code: "TwoFactor.CodeRequired",
        description: "Verification code is required.");

    public static readonly Error RecoveryCodeRequired = Error.Validation(
        code: "TwoFactor.RecoveryCodeRequired",
        description: "The recovery code is required.");
}
