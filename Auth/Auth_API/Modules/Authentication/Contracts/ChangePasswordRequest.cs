using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for changing a user's password.
/// </summary>
public record ChangePasswordRequest
{
    /// <summary>
    /// Gets the user's current password for verification.
    /// </summary>
    [Required(ErrorMessage = "Current password is required.")]
    public required string CurrentPassword { get; init; }

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
}
