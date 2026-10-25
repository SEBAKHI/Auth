namespace Auth.Application.DTOs;

/// <summary>
/// An application role a user holds through an organization
/// (<c>GET users/{id}/organization-roles</c>). Read-only on the user page: it is
/// managed on the organization's member page, which <see cref="OrganizationId"/> leads to.
/// </summary>
public class UserOrganizationRoleDto
{
    public Guid OrganizationId { get; set; }
    public string OrganizationCode { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public Guid ApplicationId { get; set; }
    public string ApplicationCode { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>Whether the application is deleted; its name is still given.</summary>
    public bool ApplicationIsDeleted { get; set; }

    public Guid RoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
}
