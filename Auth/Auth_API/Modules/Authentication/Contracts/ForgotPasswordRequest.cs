using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for initiating a password reset.
/// </summary>
public record ForgotPasswordRequest
{
    /// <summary>
    /// Gets the email address of the account to reset.
    /// </summary>
    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    public required string Email { get; init; }
}
