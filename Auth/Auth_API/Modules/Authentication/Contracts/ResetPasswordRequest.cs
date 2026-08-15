using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for resetting a password with a reset token.
/// </summary>
public record ResetPasswordRequest
{
    /// <summary>
    /// Gets the password reset token received via email.
    /// Identifies the user on its own; no email address is required.
    /// </summary>
    [Required]
    public required string Token { get; init; }

    /// <summary>
    /// Gets the new password to set. Length and complexity are the configured
    /// policy's business, enforced by PasswordValidator in the handler; a
    /// length attribute here would be a second, hardcoded floor that silently
    /// overrode Password:MinimumLength whenever an operator lowered it.
    /// </summary>
    [Required]
    public required string NewPassword { get; init; }

    /// <summary>
    /// Gets the new password confirmation (must match NewPassword).
    /// </summary>
    [Required]
    [Compare(nameof(NewPassword))]
    public required string ConfirmNewPassword { get; init; }

    /// <summary>
    /// Gets whether to terminate all sessions after resetting the password.
    /// If not specified, uses server configuration default (typically true).
    /// </summary>
    public bool? TerminateSessions { get; init; }
}
