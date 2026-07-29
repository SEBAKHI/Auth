using System.ComponentModel.DataAnnotations;

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
    [Required]
    [EmailAddress]
    [StringLength(255)]
    public required string Email { get; init; }

    /// <summary>
    /// Gets the account password.
    /// </summary>
    [Required]
    [StringLength(128)]
    public required string Password { get; init; }

    /// <summary>
    /// Gets the TOTP code (accounts with 2FA enabled).
    /// </summary>
    [StringLength(8)]
    public string? TwoFactorCode { get; init; }
}
