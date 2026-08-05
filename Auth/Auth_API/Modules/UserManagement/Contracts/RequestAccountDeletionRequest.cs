using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.UserManagement.Contracts;

/// <summary>
/// Request model for the authenticated in-app account deletion request. Every
/// account — password or external-only — confirms with the verification code
/// emailed by <c>POST me/deletion/send-code</c>.
/// </summary>
public record RequestAccountDeletionRequest
{
    /// <summary>
    /// Gets the deletion verification code emailed to the account address.
    /// </summary>
    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string OtpCode { get; init; } = string.Empty;
}
