namespace Auth.Domain.Constants;

/// <summary>
/// Stable codes of the seeded notification types. Calling code references types
/// by code (never by GUID); the codes must match the NotificationTypes seed data.
/// </summary>
public static class NotificationTypeCodes
{
    public const string EmailVerification = "email-verification";
    public const string PasswordReset = "password-reset";
    public const string OrganizationInvitation = "organization-invitation";
    public const string WelcomeEmail = "welcome-email";
    public const string OwnershipTransferCode = "ownership-transfer-code";
    public const string OwnershipTransferred = "ownership-transferred";
    public const string AccountDeletionRequested = "account-deletion-requested";
    public const string AccountDeletionVerification = "account-deletion-verification";
    public const string AccountDeletionCancelled = "account-deletion-cancelled";
    public const string AccountDeletionCompleted = "account-deletion-completed";

    /// <summary>
    /// System types that back critical auth flows; their global templates must
    /// always have a published version and cannot be unpublished or deleted.
    /// </summary>
    public static readonly IReadOnlyList<string> SystemCodes =
        [EmailVerification, PasswordReset, OrganizationInvitation, OwnershipTransferCode, OwnershipTransferred];
}
