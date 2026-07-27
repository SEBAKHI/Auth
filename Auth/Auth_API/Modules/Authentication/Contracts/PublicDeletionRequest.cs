using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for step 1 of the public no-login deletion flow.
/// </summary>
public record PublicDeletionRequest
{
    /// <summary>
    /// Gets the email address of the account to delete.
    /// </summary>
    [Required]
    [EmailAddress]
    [StringLength(255)]
    public required string Email { get; init; }
}
