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
    public const string PrivacyPolicyUpdated = "privacy-policy-updated";
    public const string NewDeviceSignIn = "new-device-sign-in";

    /// <summary>
    /// System types that back critical auth flows; their global templates must
    /// always have a published version and cannot be unpublished or deleted.
    /// </summary>
    public static readonly IReadOnlyList<string> SystemCodes =
        [
            EmailVerification, PasswordReset, OrganizationInvitation, OwnershipTransferCode, OwnershipTransferred,
            AccountDeletionRequested, AccountDeletionVerification, AccountDeletionCancelled, AccountDeletionCompleted,
            PrivacyPolicyUpdated,
            // A security notice: if its template were ever unpublished, the
            // account owner would silently stop hearing about new sign-ins.
            NewDeviceSignIn
        ];

    /// <summary>
    /// Placeholder stored in place of a sensitive rendered body once it no
    /// longer needs to exist (after delivery), and returned by the delivery-log
    /// read model for sensitive types in every status.
    /// </summary>
    public const string RedactedBody = "[redacted]";

    /// <summary>
    /// Types whose rendered bodies carry live one-time secrets — OTP codes or
    /// tokenized links that grant access on their own. Their outbox rows keep
    /// the body only while dispatch still needs it: it is replaced with
    /// <see cref="RedactedBody"/> the moment delivery succeeds, and the admin
    /// delivery log never returns it regardless of status (least privilege —
    /// an email-verification code, for one, signs the recipient in).
    /// </summary>
    public static readonly IReadOnlySet<string> SensitiveContentCodes = new HashSet<string>
    {
        EmailVerification,
        PasswordReset,
        OrganizationInvitation,
        OwnershipTransferCode,
        AccountDeletionVerification,
    };
}
