namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for organization invitation.
/// </summary>
/// <remarks>
/// There is deliberately no <c>Token</c> property, and its absence is a security
/// boundary rather than an oversight.
/// <para>
/// Registering through an invitation confirms the new account's email address
/// with no further proof, on the stated grounds that the token was delivered to
/// that mailbox and so holding it proves ownership. That argument is sound only
/// while the plaintext reaches the invited mailbox and nowhere else. This type
/// carried the plaintext back to whoever created the invitation, which made the
/// argument false and turned "invite an address" into "register that address,
/// pre-confirmed, with a password of my choosing" for any caller able to create
/// an organization — which was every signed-in user.
/// </para>
/// <para>
/// So the property is deleted rather than left unassigned: an unassigned property
/// is one line away from being filled in again by someone who reads the type as
/// the shape of an invitation rather than as the shape of what a caller may learn
/// about one.
/// </para>
/// </remarks>
public class OrganizationInvitationDto
{
    public Guid Id { get; set; }
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

    /// <summary>
    /// Stable AuthMessages resource key for <see cref="Message"/>; the API edge
    /// replaces the English fallback with the request-culture translation.
    /// </summary>
    public string? MessageCode { get; set; }
}
