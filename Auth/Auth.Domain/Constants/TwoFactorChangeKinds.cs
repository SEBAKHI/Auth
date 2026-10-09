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
}
