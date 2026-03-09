using ErrorOr;

namespace Auth_Lib.Domain.Errors;

/// <summary>
/// Domain errors related to user operations.
/// </summary>
public static class UserErrors
{
    public static Error NotFound(Guid userId) => Error.NotFound(
        code: "User.NotFound",
        description: $"User with ID '{userId}' was not found.");

    public static Error NotFoundByEmail(string email) => Error.NotFound(
        code: "User.NotFoundByEmail",
        description: $"User with email '{email}' was not found.");

    public static Error DuplicateEmail(string email) => Error.Conflict(
        code: "User.DuplicateEmail",
        description: $"A user with email '{email}' already exists.");

    public static Error InvalidCredentials => Error.Validation(
        code: "User.InvalidCredentials",
        description: "The provided credentials are invalid.");

    public static Error AccountLocked => Error.Forbidden(
        code: "User.AccountLocked",
        description: "This account has been locked due to multiple failed login attempts.");

    public static Error AccountLockedUntil(DateTime? lockoutEnd) => Error.Forbidden(
        code: "User.AccountLocked",
        description: lockoutEnd.HasValue
            ? $"This account is locked until {lockoutEnd.Value:u}."
            : "This account has been locked.");

    public static Error AccountInactive => Error.Forbidden(
        code: "User.AccountInactive",
        description: "This account is currently inactive.");

    public static Error AccountPending => Error.Forbidden(
        code: "User.AccountPending",
        description: "This account is pending activation.");

    public static Error EmailNotConfirmed => Error.Forbidden(
        code: "User.EmailNotConfirmed",
        description: "Please confirm your email address before logging in.");

    public static Error PasswordExpired => Error.Forbidden(
        code: "User.PasswordExpired",
        description: "Your password has expired. Please change your password.");

    public static Error MustChangePassword => Error.Forbidden(
        code: "User.MustChangePassword",
        description: "You must change your password before continuing.");

    public static Error InvalidCurrentPassword => Error.Validation(
        code: "User.InvalidCurrentPassword",
        description: "The current password is incorrect.");

    public static Error PasswordRecentlyUsed => Error.Validation(
        code: "User.PasswordRecentlyUsed",
        description: "This password has been used recently. Please choose a different password.");

    public static Error PasswordTooWeak => Error.Validation(
        code: "User.PasswordTooWeak",
        description: "The password does not meet the complexity requirements.");

    public static Error CannotDeleteSystemUser => Error.Forbidden(
        code: "User.CannotDeleteSystemUser",
        description: "System users cannot be deleted.");

    public static Error CannotModifySystemUser => Error.Forbidden(
        code: "User.CannotModifySystemUser",
        description: "System users cannot be modified.");

    public static Error TwoFactorRequired => Error.Forbidden(
        code: "User.TwoFactorRequired",
        description: "Two-factor authentication is required for this account.");

    public static Error InvalidTwoFactorCode => Error.Validation(
        code: "User.InvalidTwoFactorCode",
        description: "The two-factor authentication code is invalid.");

    public static Error TwoFactorAlreadyEnabled => Error.Conflict(
        code: "User.TwoFactorAlreadyEnabled",
        description: "Two-factor authentication is already enabled for this account.");

    public static Error TwoFactorNotEnabled => Error.Validation(
        code: "User.TwoFactorNotEnabled",
        description: "Two-factor authentication is not enabled for this account.");
}
