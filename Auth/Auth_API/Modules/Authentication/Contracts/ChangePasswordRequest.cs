namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for changing a user's password.
/// </summary>
public record ChangePasswordRequest
{
    /// <summary>
    /// Gets the user's current password for verification.
    /// </summary>
    public string CurrentPassword { get; init; } = string.Empty;

    /// <summary>
    /// Gets the new password to set. Length and complexity are the configured
    /// policy's business, enforced by PasswordValidator in the handler; a
    /// length attribute here would be a second, hardcoded floor that silently
    /// overrode Password:MinimumLength whenever an operator lowered it.
    /// </summary>
    public string NewPassword { get; init; } = string.Empty;

    /// <summary>
    /// Gets the new password confirmation (must match NewPassword).
    /// </summary>
    public string ConfirmNewPassword { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether to terminate all other sessions after changing the password.
    /// If not specified, uses server configuration default (typically true).
    /// When true, current session is preserved while other sessions are terminated.
    /// </summary>
    public bool? TerminateSessions { get; init; }
}
