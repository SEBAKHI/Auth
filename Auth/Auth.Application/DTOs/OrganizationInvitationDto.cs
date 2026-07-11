namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for organization invitation.
/// </summary>
public class OrganizationInvitationDto
{
    public Guid Id { get; set; }
    public string? Token { get; set; }
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string? OrganizationLogoUrl { get; set; }
    public string Email { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsExpired { get; set; }
    public Guid InvitedBy { get; set; }
    public string? InvitedByName { get; set; }
    public string? InvitedByEmail { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public Guid? AcceptedByUserId { get; set; }
    public string? AcceptedByUserName { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Invitation details for acceptance flow (limited info for security).
/// </summary>
public class InvitationPreviewDto
{
    public Guid Id { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string? OrganizationLogoUrl { get; set; }
    public string Email { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string InvitedByName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsExpired { get; set; }
    public bool IsAlreadyMember { get; set; }
    public bool UserExists { get; set; }
}

/// <summary>
/// Result of accepting an invitation.
/// </summary>
public class InvitationAcceptResultDto
{
    public bool Success { get; set; }
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string? Message { get; set; }
}
