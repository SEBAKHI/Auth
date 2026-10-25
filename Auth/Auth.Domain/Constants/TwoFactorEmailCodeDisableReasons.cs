namespace Auth.Domain.Constants;

/// <summary>
/// Why the emailed code stopped being an account's second factor: the
/// <c>Reason</c> of <see cref="Events.TwoFactorEmailCodeDisabledEvent"/>, recorded in
/// the audit row. Declared once and never localized.
/// </summary>
public static class TwoFactorEmailCodeDisableReasons
{
    /// <summary>The owner switched it off, with an emailed code or a recovery code.</summary>
    public const string User = "user";

    /// <summary>An authenticator app took its place (the two are never bound together).</summary>
    public const string ReplacedByAuthenticator = "replaced-by-authenticator";
}
