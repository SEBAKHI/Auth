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
}
