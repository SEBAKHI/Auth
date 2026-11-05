using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors of passkeys (S20): the credentials a person's device makes and keeps,
/// which sign only for this platform's domain.
/// </summary>
public static class PasskeyErrors
{
    /// <summary>
    /// The one answer to every ceremony that did not verify (PK10): a malformed or
    /// mismatched response, a wrong origin, no user verification, an algorithm other than
    /// ES256 or RS256, a challenge already spent or expired, a credential already
    /// registered. One code, so the answer never tells which check failed. Validation,
    /// not Unauthorized: the registration is made inside a signed-in session, and the
    /// clients refresh and replay on 401.
    /// </summary>
    public static readonly Error VerificationFailed = Error.Validation(
        code: "Passkey.VerificationFailed",
        description: "The passkey could not be verified. Try again.");

    /// <summary>The account already has the most live passkeys it may hold.</summary>
    public static readonly Error LimitReached = Error.Conflict(
        code: "Passkey.LimitReached",
        description: "This account already has the maximum number of passkeys. Remove one before adding another.");

    /// <summary>
    /// With email sending off, a passkey is never an account's first qualifying factor
    /// (REVIEW 150 F13, D-150-3 (c)): the code that proves the mailbox cannot be sent, and a
    /// passkey bound from a password sign-in alone would be a second door nobody is told of.
    /// The account adds the authenticator app first (it binds without a code while email is
    /// off, RV-P8-4), then a passkey from a session that proved both. Conflict: the account's
    /// state and the deployment's, not the request, forbid it.
    /// </summary>
    public static readonly Error EmailRequired = Error.Conflict(
        code: "Passkey.EmailRequired",
        description: "A first passkey can be added only while email sending is on. Add the authenticator app first, then the passkey.");

    /// <summary>
    /// No live passkey of the caller has this id: never another account's, never a removed
    /// one, never one bound to another relying party.
    /// </summary>
    public static readonly Error NotFound = Error.NotFound(
        code: "Passkey.NotFound",
        description: "The passkey was not found.");

    // Request-validation rules (ADR 0001): validators declare these with
    // WithErrorCode, and the validation behavior carries the offending property.

    /// <summary>A passkey's name is longer than 64 characters (counted as Unicode code points).</summary>
    public static readonly Error NameTooLong = Error.Validation(
        code: "Passkey.NameTooLong",
        description: "A passkey name must not exceed 64 characters.");
}
