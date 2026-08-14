using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

/// <summary>
/// One standing invitation on a restricted application's access list.
/// </summary>
public class ApplicationAccessGrantDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FullName { get; set; }
    public string? DisplayName { get; set; }
    public string? ProfileImageUrl { get; set; }
    public UserStatus Status { get; set; }

    public DateTime GrantedAt { get; set; }
    public Guid GrantedBy { get; set; }

    /// <summary>Display name of the inviting administrator; null when unresolved.</summary>
    public string? GrantedByName { get; set; }

    /// <summary>
    /// When the invitation lapses on its own. Null means it stands until
    /// withdrawn.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Optional free-text reason recorded with the invitation.</summary>
    public string? Note { get; set; }
}
