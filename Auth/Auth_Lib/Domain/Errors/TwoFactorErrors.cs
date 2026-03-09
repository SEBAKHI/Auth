using ErrorOr;

namespace Auth_Lib.Domain.Errors;

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

    public static Error LockedOut => Error.Forbidden(
        code: "TwoFactor.LockedOut",
        description: "Two-factor authentication has been temporarily locked due to too many failed attempts.");

    public static Error InvalidRecoveryCode => Error.Validation(
        code: "TwoFactor.InvalidRecoveryCode",
        description: "The recovery code is invalid.");

    public static Error NoRecoveryCodesAvailable => Error.Validation(
        code: "TwoFactor.NoRecoveryCodesAvailable",
        description: "No recovery codes are available. Please contact support.");
}
