using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for step 2 of the public no-login deletion flow: confirm
/// email possession with the verification code.
/// </summary>
public record ConfirmPublicDeletionRequest
{
    /// <summary>
    /// Gets the email address of the account to delete.
    /// </summary>
    [Required]
    [EmailAddress]
    [StringLength(255)]
    public required string Email { get; init; }

    /// <summary>
    /// Gets the 6-digit verification code.
    /// </summary>
    [Required]
    [StringLength(6)]
    public required string OtpCode { get; init; }
}
