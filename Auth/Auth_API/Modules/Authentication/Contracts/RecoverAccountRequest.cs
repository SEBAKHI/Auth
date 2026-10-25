namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for recovering an account pending deletion during its grace
/// window, authenticated by password (and TOTP when 2FA is enabled).
/// </summary>
public record RecoverAccountRequest
{
    /// <summary>
    /// Gets the email address of the account to recover.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Gets the account password.
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Gets the second-factor code (accounts with 2FA enabled): six digits for the
    /// account's own factor — the authenticator app's code, or a code emailed for this
    /// recovery when the factor is email — or a recovery code. At most 16 characters.
    /// </summary>
    public string? TwoFactorCode { get; init; }

    /// <summary>
    /// Gets whether to email a recovery code instead of recovering now, for an account
    /// whose second factor is the emailed code (S24). Answered 202 with where the code
    /// went; the next request carries the code in <see cref="TwoFactorCode"/>.
    /// </summary>
    public bool SendEmailCode { get; init; }
}
