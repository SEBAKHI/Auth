namespace Auth.Domain.Constants;

/// <summary>
/// The values of the <c>ChangeKind</c> variable of the
/// <see cref="NotificationTypeCodes.TwoFactorChanged"/> notice. Its templates branch
/// on these exact strings, so they are declared once and never localized.
/// </summary>
/// <remarks>
/// The templates also know <c>recovery-codes-regenerated</c>,
/// <c>authenticator-replaced</c> and <c>reset-by-administrator</c>; they join this
/// list with the paths that send them.
/// </remarks>
public static class TwoFactorChangeKinds
{
    /// <summary>Two-factor authentication was switched on.</summary>
    public const string Enabled = "enabled";

    /// <summary>Two-factor authentication was switched off.</summary>
    public const string Disabled = "disabled";
}
