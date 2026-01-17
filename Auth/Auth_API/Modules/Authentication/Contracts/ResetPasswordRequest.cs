using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for resetting a password with a reset token.
/// </summary>
public record ResetPasswordRequest
{
    /// <summary>
    /// Gets the password reset token received via email.
    /// </summary>
    [Required(ErrorMessage = "Reset token is required.")]
    public required string Token { get; init; }

    /// <summary>
    /// Gets the new password to set.
    /// </summary>
    [Required(ErrorMessage = "New password is required.")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters long.")]
    public required string NewPassword { get; init; }

    /// <summary>
    /// Gets the new password confirmation (must match NewPassword).
    /// </summary>
    [Required(ErrorMessage = "Password confirmation is required.")]
    [Compare(nameof(NewPassword), ErrorMessage = "New password and confirmation do not match.")]
    public required string ConfirmNewPassword { get; init; }

    /// <summary>
    /// Gets whether to terminate all sessions after resetting the password.
    /// If not specified, uses server configuration default (typically true).
    /// </summary>
    public bool? TerminateSessions { get; init; }
}
