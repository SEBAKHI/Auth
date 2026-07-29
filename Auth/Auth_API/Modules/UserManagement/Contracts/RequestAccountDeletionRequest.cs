using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.UserManagement.Contracts;

/// <summary>
/// Request model for the authenticated in-app account deletion request.
/// Password accounts confirm with their current password; passwordless
/// (external-only) accounts confirm with an emailed verification code.
/// </summary>
public record RequestAccountDeletionRequest
{
    /// <summary>
    /// Gets the current password (password accounts).
    /// </summary>
    [StringLength(128)]
    public string? Password { get; init; }

    /// <summary>
    /// Gets the deletion verification code (passwordless accounts).
    /// </summary>
    [StringLength(6)]
    public string? OtpCode { get; init; }
}
