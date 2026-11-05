namespace Auth.Domain.Constants;

/// <summary>
/// The event codes of the passkey log lines (S20, R-SIG), written as <c>{EventCode}</c> in
/// the message template so an alert rule matches the code and never the wording.
/// </summary>
/// <remarks>
/// <para>
/// The values are part of the operator's contract: a value is never renamed or reused.
/// Until the alert catalogue ships (S16, S17), these are plain Information, Warning and
/// Error lines (ARR-0701); <c>PasskeySignalCodesTests</c> pins each one and the format.
/// </para>
/// <para>
/// A line carries the account id and the passkey's row id. It never carries a credential
/// id, a public key, a user handle or client data (PK16), nor the email address or the
/// client address.
/// </para>
/// </remarks>
public static class PasskeySignalCodes
{
    /// <summary>A passkey was registered. Information.</summary>
    public const string Registered = "passkey.registered";

    /// <summary>A passkey was removed, with its reason. Information.</summary>
    public const string Removed = "passkey.removed";

    /// <summary>A registration did not verify (PK10). Information; the alert rule is A121 (S16).</summary>
    public const string VerificationFailed = "passkey.verification-failed";

    /// <summary>
    /// Passkeys:Enabled is on but the feature cannot run: no RP ID, no first-party origin, an
    /// RP ID that does not cover every origin, or no schema (PK19, A125). Critical.
    /// </summary>
    public const string Configuration = "passkey.configuration";

    /// <summary>
    /// Live passkeys are bound to an RP ID other than Passkeys:RpId (PK3, A124): they no
    /// longer sign in or count as a factor. Critical.
    /// </summary>
    public const string RpIdMismatch = "passkey.rpid-mismatch";

    /// <summary>
    /// A password reset, a password change or an administrator's lock completed, but the
    /// passkeys registered in the 72 hours before it could not be revoked. Error.
    /// </summary>
    public const string RevocationFailed = "passkey.revocation-failed";
}
