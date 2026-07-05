namespace Auth.Application.DTOs;

/// <summary>
/// One application related to a role: its owning application and/or an
/// application appearing on active assignments of the role.
/// </summary>
public class RoleApplicationDto
{
    public Guid ApplicationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// How the application relates to the role: "owner", "assigned", or "both".
    /// </summary>
    public string Relationship { get; set; } = string.Empty;
}
