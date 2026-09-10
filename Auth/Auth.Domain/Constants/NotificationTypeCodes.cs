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
    /// An administrator permanently destroyed the account. Deliberately a
    /// SEPARATE type from <see cref="AccountDeletionCompleted"/>, whose copy
    /// says "as you requested" — true of the self-service path and false here.
    /// Telling someone their own deletion request was honoured when an admin
    /// deleted them is a worse disclosure than sending nothing.
    /// </summary>
    public const string AccountDeletedByAdmin = "account-deleted-by-admin";
    public const string PrivacyPolicyUpdated = "privacy-policy-updated";
    public const string NewDeviceSignIn = "new-device-sign-in";

    /// <summary>
    /// A spent refresh token was presented a second time, so every token and
    /// session the account held was revoked. Distinct from a voluntary
    /// "sign out everywhere": the owner did not ask for this and was signed out
    /// of every device on the strength of a suspicion, which is precisely why
    /// they have to hear about it.
    /// </summary>
    public const string SessionsRevokedTokenReuse = "sessions-revoked-token-reuse";

    /// <summary>
    /// A sign-in pushed the account past Session:MaxConcurrentSessions, so its
    /// least recently used sessions were ended to make room. Separate from
    /// <see cref="SessionsRevokedTokenReuse"/>, which reports a suspected theft:
    /// this one is ordinary policy, and telling someone their account may be
    /// compromised when they simply signed in on a fifth device would be a false
    /// alarm. The owner still has to hear it — a device signed out without them
    /// touching it looks exactly like an intrusion until it is explained.
    /// </summary>
    public const string SessionLimitEnforced = "session-limit-enforced";

    /// <summary>
    /// A one-time code confirming a destructive secret operation — regenerating
    /// or importing the RSA signing key, the refresh-token HMAC key, or the
    /// gateway token. Sent to the administrator who asked for it, and to nobody
    /// else: the point is that holding the console session is not enough.
    /// </summary>
    public const string SecretOperationChallenge = "secret-operation-challenge";

    /// <summary>
    /// A password was added to an account that had none — an external-only (Google, Apple)
    /// sign-up acquiring local credentials. A SEPARATE type from
    /// <see cref="PasswordChanged"/> on purpose: the account did not merely rotate a secret,
    /// it became reachable by a second means it was not reachable by before, and only the
    /// owner can say whether that was them.
    /// </summary>
    public const string PasswordCreated = "password-created";

    /// <summary>
    /// An existing password was replaced, whether from the profile screen or through a reset
    /// link. Until this type existed the system changed passwords in total silence.
    /// </summary>
    public const string PasswordChanged = "password-changed";

    /// <summary>
    /// The one-time code that starts a self-registration, sent to an address that
    /// has no account yet. A SEPARATE type from <see cref="EmailVerification"/> on
    /// purpose: that one greets an existing user by name and confirms an address
    /// an account already holds, while this one is the only thing standing between
    /// a stranger's typing and a Users row — no row, no name, no account exists
    /// when it is sent. Its rendered body carries the live code, so it is a
    /// sensitive type and its outbox body is redacted like every other OTP.
    /// </summary>
    public const string RegistrationVerification = "registration-verification";

    /// <summary>
    /// Sent instead of a code when the typed address cannot be used to create a
    /// new account: it already belongs to one, or it is reserved after a deletion.
    /// ONE type for both cases, so that the two cost the same on the wire and in
    /// the outbox and the copy names neither; the response to the caller is the
    /// same as for a free address. Written as an ordinary notice rather than a
    /// security alert — nothing happened to the account — with links to ordinary
    /// pages only, never to a one-click action a mail scanner's prefetch could fire.
    /// </summary>
    public const string RegistrationAttemptExistingAccount = "registration-attempt-existing-account";

    /// <summary>
    /// System types that back critical auth flows; their global templates must
    /// always have a published version and cannot be unpublished or deleted.
    /// </summary>
    public static readonly IReadOnlyList<string> SystemCodes =
        [
            EmailVerification, PasswordReset, OrganizationInvitation, OwnershipTransferCode, OwnershipTransferred,
            AccountDeletionRequested, AccountDeletionVerification, AccountDeletionCancelled, AccountDeletionCompleted,
            AccountDeletedByAdmin,
            PrivacyPolicyUpdated,
            // A security notice: if its template were ever unpublished, the
            // account owner would silently stop hearing about new sign-ins.
            NewDeviceSignIn,
            // Likewise: unpublishing this one would mean accounts get signed
            // out of every device over a suspected theft, in silence.
            SessionsRevokedTokenReuse,
            // And this one: a device dropping out of a signed-in account with no
            // explanation is indistinguishable from being hijacked.
            SessionLimitEnforced,
            // Unpublishing this one would leave the platform's signing keys
            // behind a confirmation code that can never be delivered — the
            // operation would be unavailable rather than unprotected, but the
            // control is not something an operator should be able to switch off
            // from the template screen either way.
            SecretOperationChallenge,
            // Both password notices: an account's credentials changing without its owner
            // hearing about it is the quietest possible takeover, and these are the only
            // messages that break that silence.
            PasswordCreated,
            PasswordChanged,
            // Self-registration cannot begin without the code, and the notice is
            // the only thing that tells an account owner someone typed their
            // address into the sign-up form. Neither is an operator's to switch off.
            RegistrationVerification,
            RegistrationAttemptExistingAccount
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
        SecretOperationChallenge,
        // The code that lets a stranger's address become an account. The notice
        // that goes to an existing owner is deliberately NOT here: it carries no
        // secret, only links to ordinary pages, and an admin reading the delivery
        // log has to be able to see what an owner was told.
        RegistrationVerification,
    };
}
