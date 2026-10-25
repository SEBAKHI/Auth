namespace Auth.Domain.Constants;

/// <summary>
/// The values of the <c>ChangeKind</c> variable of the
/// <see cref="NotificationTypeCodes.TwoFactorChanged"/> notice. Its templates branch
/// on these exact strings, so they are declared once and never localized.
/// </summary>
public static class TwoFactorChangeKinds
{
    /// <summary>Two-factor authentication was switched on.</summary>
    public const string Enabled = "enabled";

    /// <summary>Two-factor authentication was switched off.</summary>
    public const string Disabled = "disabled";

    /// <summary>A new set of recovery codes replaced the old one.</summary>
    public const string RecoveryCodesRegenerated = "recovery-codes-regenerated";

    /// <summary>A new authenticator app replaced the one the factor was bound to.</summary>
    public const string AuthenticatorReplaced = "authenticator-replaced";

    /// <summary>An administrator removed the account's second factor.</summary>
    public const string ResetByAdministrator = "reset-by-administrator";

    /// <summary>
    /// The emailed code became the second factor (S24), in place of an authenticator
    /// app when one was bound.
    /// </summary>
    public const string EmailCodeEnabled = "email-code-enabled";

    /// <summary>The owner switched the emailed code off (S24).</summary>
    public const string EmailCodeDisabled = "email-code-disabled";

    /// <summary>
    /// An authenticator app took the place of the emailed code (S24): the codes by
    /// email and the recovery codes issued with them stopped working.
    /// </summary>
    public const string AuthenticatorReplacedEmailCode = "authenticator-replaced-email-code";
}
