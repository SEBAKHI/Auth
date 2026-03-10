using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to email verification operations.
/// </summary>
public static class EmailVerificationErrors
{
    public static Error InvalidOrExpiredOtp => Error.Validation(
        code: "EmailVerification.InvalidOrExpiredOtp",
        description: "The verification code is invalid or has expired.");

    public static Error OtpAlreadyUsed => Error.Validation(
        code: "EmailVerification.OtpAlreadyUsed",
        description: "This verification code has already been used.");

    public static Error TooManyAttempts => Error.Validation(
        code: "EmailVerification.TooManyAttempts",
        description: "Too many verification attempts. Please request a new code.");

    public static Error TooManyRequests => Error.Validation(
        code: "EmailVerification.TooManyRequests",
        description: "Too many verification code requests. Please try again later.");

    public static Error EmailAlreadyVerified => Error.Validation(
        code: "EmailVerification.EmailAlreadyVerified",
        description: "This email address has already been verified.");

    public static Error NoActiveToken => Error.Validation(
        code: "EmailVerification.NoActiveToken",
        description: "No active verification code found. Please request a new code.");

    public static Error EmailSendFailed => Error.Failure(
        code: "EmailVerification.EmailSendFailed",
        description: "Failed to send verification email. Please try again later.");

    public static Error UserNotFound => Error.NotFound(
        code: "EmailVerification.UserNotFound",
        description: "User not found.");

    public static Error InvalidOtpFormat => Error.Validation(
        code: "EmailVerification.InvalidOtpFormat",
        description: "The verification code must be exactly 6 digits.");
}
