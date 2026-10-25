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

    /// <summary>
    /// The account holds platform permissions and <c>TwoFactor:EnforceForPlatformAdmins</c>
    /// is on: its session has not proved a second factor, so the token carries none
    /// of that authority (the refusal of such a token), or the factor cannot be
    /// switched off (the refusal of a disable). Forbidden, not Unauthorized: the
    /// clients refresh on 401, and a refresh mints the same withheld token again.
    /// </summary>
    public static readonly Error RequiredByPolicy = Error.Forbidden(
        code: "TwoFactor.RequiredByPolicy",
        description: "Platform administrators must use two-factor authentication. Complete it to continue.");

    /// <summary>
    /// An administrator may not remove this account's second factor: it is their
    /// own (another administrator, or the owner's emergency script, does that), it
    /// is the internal system account, or it holds platform authority the
    /// administrator's own does not cover — resetting it would hand that authority
    /// to whoever sets the next factor up.
    /// </summary>
    public static readonly Error ResetNotPermitted = Error.Forbidden(
        code: "TwoFactor.ResetNotPermitted",
        description: "You cannot reset two-factor authentication for this account. Ask an administrator with at least the same permissions.");

    /// <summary>
    /// The code that confirms a new authenticator arrived with no replacement
    /// waiting: none was started, it was confirmed already, or it is older than
    /// <c>TwoFactorAuth.PendingReplacementLifetimeMinutes</c>. Start the
    /// replacement again. Usually nothing was counted; when the replacement
    /// expired, was started afresh, or was confirmed by another request — or the
    /// recovery codes were renewed — after this one's attempt was reserved, that
    /// attempt stays counted.
    /// </summary>
    public static readonly Error NoPendingReplacement = Error.Conflict(
        code: "TwoFactor.NoPendingReplacement",
        description: "There is no authenticator replacement waiting to be confirmed, or it has expired. Start the replacement again.");

    /// <summary>
    /// While <c>TwoFactor:EnforceForPlatformAdmins</c> is on, platform authority
    /// goes only to accounts that already have their own second factor: a role or
    /// permission given to an account without one could be claimed by whoever
    /// sets a factor up first. The same answer when a permission is added to a
    /// platform role one of whose holders has no factor. The account sets up
    /// two-step verification, then the grant is made.
    /// </summary>
    public static readonly Error RequiredForPlatformGrant = Error.Conflict(
        code: "TwoFactor.RequiredForPlatformGrant",
        description: "Platform permissions can only go to accounts that use two-factor authentication. Every account that would receive them must turn it on first.");

    public static Error LockedOut => Error.Forbidden(
        code: "TwoFactor.LockedOut",
        description: "Two-factor authentication has been temporarily locked due to too many failed attempts.");

    /// <summary>
    /// The second factor reached the consecutive-failure limit
    /// (<c>Password:ConsecutiveFailureLimit</c>, NIST SP 800-63B-4 §3.2.2): it accepts
    /// no code — from the authenticator app or a recovery code — and waiting does
    /// not end it. Whoever got this far passed the first sign-in step (the password,
    /// or a sign-in provider linked to the account), so the way out
    /// starts with a password reset by email; then an administrator resets the
    /// factor. The sentence names both steps, because a lock with no named exit is
    /// a dead end.
    /// </summary>
    public static readonly Error LockedAdminResetRequired = Error.Forbidden(
        code: "TwoFactor.LockedAdminResetRequired",
        description: "Two-factor authentication is locked after too many wrong codes. Reset your password from the sign-in page, then ask an administrator to reset two-factor authentication.");

    /// <summary>
    /// An administrator asked to reset a second factor that reached the
    /// consecutive-failure limit before the account's owner reset the password.
    /// Whoever drove the factor to the limit knew the password, so resetting the
    /// factor first would hand the account to them; the owner resets the password
    /// by email first (LK4 (4)). Nothing was revoked or removed.
    /// </summary>
    public static readonly Error PasswordResetRequiredFirst = Error.Forbidden(
        code: "TwoFactor.PasswordResetRequiredFirst",
        description: "This account's two-factor authentication is locked after too many wrong codes. Its owner must reset the password by email before you can reset two-factor authentication.");

    public static Error InvalidRecoveryCode => Error.Validation(
        code: "TwoFactor.InvalidRecoveryCode",
        description: "The recovery code is invalid.");

    public static Error NoRecoveryCodesAvailable => Error.Validation(
        code: "TwoFactor.NoRecoveryCodesAvailable",
        description: "No recovery codes are available. Please contact support.");

    /// <summary>
    /// A correct authenticator-app code whose time step was already accepted: the
    /// same code presented again, or an older one after a newer. The warning is
    /// deliberate — only the code's owner sees it, and a reuse they did not make
    /// means someone else saw the code and holds the password.
    /// </summary>
    public static readonly Error CodeAlreadyUsed = Error.Validation(
        code: "TwoFactor.CodeAlreadyUsed",
        description: "This code was already used. Wait for the next code. If you did not just use it, change your password.");

    // The code emailed before an account binds its FIRST second factor. It proves
    // the mailbox for that bind only: it is never a factor and signs nobody in.

    /// <summary>
    /// The account has no factor yet and binding its first one needs the emailed
    /// code, but the request carried none. Nothing was counted: a request without
    /// the code can never succeed. Also the answer a client built before the email
    /// step existed receives.
    /// </summary>
    public static readonly Error EmailCodeRequired = Error.Validation(
        code: "TwoFactor.EmailCodeRequired",
        description: "To turn on two-factor authentication, also enter the code sent to your email address. Send the code, then try again.");

    /// <summary>
    /// The single failure shape for the emailed code: wrong, expired, superseded by
    /// a newer one, already spent, out of attempts, or never sent. Telling them
    /// apart would tell a guesser which of their assumptions was right.
    /// </summary>
    public static readonly Error EmailCodeInvalid = Error.Validation(
        code: "TwoFactor.EmailCodeInvalid",
        description: "The email code is incorrect or is no longer valid. Send a new code and try again.");

    /// <summary>
    /// The per-account cap on issued codes was reached. It guards the mailbox
    /// from a flood of codes, whoever asks for them. The wait is
    /// Email:RateLimitWindowSeconds, which an operator can change, so the text
    /// names no duration.
    /// </summary>
    public static readonly Error EmailCodeTooManyRequests = Error.Forbidden(
        code: "TwoFactor.EmailCodeTooManyRequests",
        description: "Too many email codes were requested. Please wait before asking for another.");

    /// <summary>
    /// The account has no confirmed address to send the code to, so a code is
    /// never sent to an address nobody proved. A password sign-in requires a
    /// confirmed address, but a provider sign-in that linked an existing account
    /// does not, so this is reachable: the security tab offers to confirm the
    /// address first.
    /// </summary>
    public static readonly Error EmailCodeRecipientUnavailable = Error.Conflict(
        code: "TwoFactor.EmailCodeRecipientUnavailable",
        description: "Your account has no confirmed email address, so a code cannot be sent. Confirm your email address before turning on two-factor authentication.");

    public static readonly Error EmailCodeSendFailed = Error.Failure(
        code: "TwoFactor.EmailCodeSendFailed",
        description: "The email with your code could not be sent. Please try again later, or contact your administrator if it keeps failing.");

    // The code emailed as the SECOND factor of an account that chose it (S24), at
    // sign-in and when an account waiting for deletion is recovered. Weaker than the
    // authenticator app: whoever holds the mailbox holds the password reset, so it
    // never satisfies a two-factor policy. Its failures are counted apart from the
    // authenticator app's (TwoFactorAuth.EmailFailedAttempts).

    /// <summary>
    /// No code can be emailed for this sign-in: email sending is off, the account's
    /// second factor is not the emailed code, or the sign-in started with a provider
    /// or an emailed link, after which a code to the same mailbox proves nothing new.
    /// Nothing was sent or counted. The way on is a recovery code.
    /// </summary>
    public static readonly Error EmailCodeUnavailable = Error.Conflict(
        code: "TwoFactor.EmailCodeUnavailable",
        description: "A code cannot be emailed for this sign-in. Use one of your recovery codes instead.");

    /// <summary>
    /// The emailed code reached the consecutive-failure limit (Hard′) and accepts no
    /// code until the password is reset, which clears its count. Waiting does not end
    /// it; a recovery code still works.
    /// </summary>
    public static readonly Error EmailCodeLocked = Error.Forbidden(
        code: "TwoFactor.EmailCodeLocked",
        description: "Too many wrong email codes were entered. Use a recovery code, or reset your password to use email codes again.");

    /// <summary>
    /// The code is one this challenge emailed before a newer one replaced it. Not
    /// counted: only the holder of the mailbox could present it, so it is no guess.
    /// The newest email carries the code to enter.
    /// </summary>
    public static readonly Error EmailCodeSuperseded = Error.Validation(
        code: "TwoFactor.EmailCodeSuperseded",
        description: "This code was replaced by a newer one. Enter the code from the most recent email.");

    /// <summary>
    /// The account waiting for deletion has the emailed code as its second factor and
    /// the recovery request carried no code. Nothing was counted. Ask for a code with
    /// the same password, or enter a recovery code.
    /// </summary>
    public static readonly Error RecoveryEmailCodeRequired = Error.Validation(
        code: "TwoFactor.RecoveryEmailCodeRequired",
        description: "Enter the code sent to your email address, or one of your recovery codes. Ask for a code first if you have none.");

    /// <summary>
    /// The emailed code cannot become the second factor of an account without a
    /// password: such an account signs in through a provider or an emailed link,
    /// which already went through the same mailbox (D-T35 A), so the code would add
    /// nothing. Setting a password first makes it available.
    /// </summary>
    public static readonly Error EmailCodeRequiresPassword = Error.Conflict(
        code: "TwoFactor.EmailCodeRequiresPassword",
        description: "Set a password for your account before turning on codes by email.");

    /// <summary>
    /// The account's second factor is the emailed code, and switching to an
    /// authenticator app replaces it: the request must also carry a code emailed for
    /// that (POST 2fa/email/disable without a code sends one) or a recovery code.
    /// Nothing was counted.
    /// </summary>
    public static readonly Error ReplacementProofRequired = Error.Validation(
        code: "TwoFactor.ReplacementProofRequired",
        description: "Your second step is a code sent by email. To switch to an authenticator app, also enter a code we email you, or one of your recovery codes.");

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

    public static readonly Error EmailCodeInvalidFormat = Error.Validation(
        code: "TwoFactor.EmailCodeInvalidFormat",
        description: "The email code must be a 6-digit number.");

    public static readonly Error MethodInvalid = Error.Validation(
        code: "TwoFactor.MethodInvalid",
        description: "The verification method must be \"totp\", \"recovery\" or \"email\".");

    public static readonly Error CodeTooLong = Error.Validation(
        code: "TwoFactor.CodeTooLong",
        description: "The code must be at most 16 characters.");
}
