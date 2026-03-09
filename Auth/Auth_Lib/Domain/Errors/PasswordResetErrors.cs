using ErrorOr;

namespace Auth_Lib.Domain.Errors;

/// <summary>
/// Domain errors related to password reset operations.
/// </summary>
public static class PasswordResetErrors
{
    public static Error InvalidOrExpiredToken => Error.Validation(
        code: "PasswordReset.InvalidOrExpiredToken",
        description: "The password reset token is invalid or has expired.");

    public static Error TokenAlreadyUsed => Error.Validation(
        code: "PasswordReset.TokenAlreadyUsed",
        description: "This password reset token has already been used.");

    public static Error TooManyRequests => Error.Validation(
        code: "PasswordReset.TooManyRequests",
        description: "Too many password reset requests. Please try again later.");
}
