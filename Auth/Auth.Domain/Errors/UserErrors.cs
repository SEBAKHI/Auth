using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to user operations.
/// </summary>
public static class UserErrors
{
    public static Error NotFound(Guid userId) => Error.NotFound(
        code: "User.NotFound",
        description: $"User with ID '{userId}' was not found.",
        metadata: new() { ["args"] = new object[] { userId } });

    public static Error NotFoundByEmail(string email) => Error.NotFound(
        code: "User.NotFoundByEmail",
        description: $"User with email '{email}' was not found.",
        metadata: new() { ["args"] = new object[] { email } });

    public static Error DuplicateEmail(string email) => Error.Conflict(
        code: "User.DuplicateEmail",
        description: $"A user with email '{email}' already exists.",
        metadata: new() { ["args"] = new object[] { email } });

    /// <summary>
    /// The public sign-up door is shut by configuration. Returned before any
    /// work is done, so the refusal costs no password hash, no row and no
    /// email — and before the duplicate-email check, so a closed server answers
    /// every address identically and tells no one who is registered here.
    /// </summary>
    public static Error SelfRegistrationClosed => Error.Forbidden(
        code: "User.SelfRegistrationClosed",
        description: "New accounts cannot be created on this server.");

    /// <summary>
    /// A provider identity that matches no account here, on a server that does
    /// not create accounts from providers. Distinct from
    /// <see cref="SelfRegistrationClosed"/> because the person did authenticate
    /// successfully — with Google or Apple — and needs to know that the missing
    /// piece is an account, not a credential.
    /// </summary>
    public static Error ExternalRegistrationClosed => Error.Forbidden(
        code: "User.ExternalRegistrationClosed",
        description: "This provider account is not linked to any account on this server, and new accounts cannot be created from a provider.");

    /// <summary>
    /// The invitation door is shut by configuration. Returned before the token is
    /// looked up, so a closed server cannot be used to test whether a token is
    /// real.
    /// </summary>
    /// <remarks>
    /// The message tells the holder their invitation is not the problem, because
    /// it is not: they hold a genuine one and nothing they can do will redeem it
    /// here. Someone at the organization has to be told, and the message points
    /// there rather than leaving them retrying.
    /// </remarks>
    public static Error InvitationRegistrationClosed => Error.Forbidden(
        code: "User.InvitationRegistrationClosed",
        description: "Accounts cannot be created from invitations on this server. Ask the organization that invited you to add you another way.");

    public static Error InvalidCredentials => Error.Validation(
        code: "User.InvalidCredentials",
        description: "The provided credentials are invalid.");

    public static Error AccountLocked => Error.Forbidden(
        code: "User.AccountLocked",
        description: "This account has been locked due to multiple failed login attempts.");

    public static Error AccountLockedUntil(DateTime? lockoutEnd) => Error.Forbidden(
        code: lockoutEnd.HasValue ? "User.AccountLockedUntil" : "User.AccountLocked",
        description: lockoutEnd.HasValue
            ? $"This account is locked until {lockoutEnd.Value:u}."
            : "This account has been locked.",
        metadata: lockoutEnd.HasValue
            ? new() { ["args"] = new object[] { lockoutEnd.Value.ToString("u") } }
            : null);

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

    public static Error PasswordBreached => Error.Validation(
        code: "User.PasswordBreached",
        description: "This password has appeared in a known data breach. Please choose a different password.");

    public static Error PasswordBreachCheckUnavailable => Error.Failure(
        code: "User.PasswordBreachCheckUnavailable",
        description: "The password security check is temporarily unavailable. Please try again later.");

    /// <summary>
    /// Guards User.SetInitialPassword, which exists only for accounts that have never had a
    /// password (external-only sign-ups). Rotating an existing one is User.ChangePassword,
    /// whose caller proves knowledge of the current password first.
    /// </summary>
    public static Error PasswordAlreadySet => Error.Conflict(
        code: "User.PasswordAlreadySet",
        description: "This account already has a password. Change it instead of setting a new one.");

    public static Error CannotDeleteSystemUser => Error.Forbidden(
        code: "User.CannotDeleteSystemUser",
        description: "System users cannot be deleted.");

    public static Error CannotDeleteOrganizationOwner => Error.Conflict(
        code: "User.CannotDeleteOrganizationOwner",
        description: "This account owns one or more organizations. Transfer their ownership before deleting the account.");

    public static Error CannotDeletePersonalOrganizationWithMembers => Error.Conflict(
        code: "User.CannotDeletePersonalOrganizationWithMembers",
        description: "This account's personal organization still has other members. Remove them before deleting the account.");

    public static Error CannotModifySystemUser => Error.Forbidden(
        code: "User.CannotModifySystemUser",
        description: "System users cannot be modified.");

    public static Error NotSoftDeleted => Error.Conflict(
        code: "User.NotSoftDeleted",
        description: "Only deleted accounts can be permanently removed. Delete the account first.");

    public static Error DeletedUsersViewNotAllowed => Error.Forbidden(
        code: "User.DeletedUsersViewNotAllowed",
        description: "Viewing deleted accounts requires user management permission.");

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

    public static Error DeletionAlreadyRequested => Error.Conflict(
        code: "User.DeletionAlreadyRequested",
        description: "An account deletion request is already pending for this account.");

    public static Error AccountPendingDeletion(DateTime graceEndsAtUtc) => Error.Forbidden(
        code: "User.AccountPendingDeletion",
        description: $"This account is deactivated and scheduled for deletion on {graceEndsAtUtc:u}. It can be restored until then.",
        metadata: new() { ["args"] = new object[] { graceEndsAtUtc.ToString("u") } });

    public static Error RecoveryWindowExpired => Error.Forbidden(
        code: "User.RecoveryWindowExpired",
        description: "The recovery period for this account has ended. Deletion is being finalized.");
}
