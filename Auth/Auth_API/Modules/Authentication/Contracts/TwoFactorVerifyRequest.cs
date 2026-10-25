namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for switching two-factor authentication on.
/// </summary>
public record TwoFactorVerifyRequest
{
    /// <summary>
    /// The 6-digit TOTP code from the authenticator app.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// The 6-digit code emailed to the account's confirmed address
    /// (<c>POST /api/v1/auth/2fa/email-code</c>). Needed only for the account's
    /// first second factor while email is on — the setup response says so in
    /// <c>emailCodeRequired</c>. Without it such a request answers 400
    /// <c>TwoFactor.EmailCodeRequired</c>.
    /// </summary>
    public string? EmailCode { get; init; }

    /// <summary>
    /// For an account whose second factor is the emailed code, which the
    /// authenticator app replaces: a code emailed for that
    /// (<c>POST /api/v1/auth/2fa/email/disable</c> with no code sends one), or one of
    /// the recovery codes when <see cref="ReplacementProofIsRecoveryCode"/> is true.
    /// Without it such a request answers 400 <c>TwoFactor.ReplacementProofRequired</c>.
    /// An emailed code here also proves the mailbox, so <see cref="EmailCode"/> is then
    /// never needed; with a recovery code it may still be.
    /// </summary>
    public string? ReplacementProofCode { get; init; }

    /// <summary>
    /// Whether <see cref="ReplacementProofCode"/> is a recovery code.
    /// </summary>
    public bool ReplacementProofIsRecoveryCode { get; init; }
}
