using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for verifying email with OTP.
/// </summary>
public record VerifyEmailRequest
{
    /// <summary>
    /// Gets the user ID to verify email for.
    /// </summary>
    [Required]
    public required Guid UserId { get; init; }

    /// <summary>
    /// Gets the 6-digit OTP code sent to the user's email.
    /// </summary>
    [Required]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "OTP must be exactly 6 digits")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "OTP must be 6 digits")]
    public required string Otp { get; init; }
}
