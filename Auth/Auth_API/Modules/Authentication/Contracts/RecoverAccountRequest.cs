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
    /// Gets the TOTP code (accounts with 2FA enabled).
    /// </summary>
    public string? TwoFactorCode { get; init; }
}
