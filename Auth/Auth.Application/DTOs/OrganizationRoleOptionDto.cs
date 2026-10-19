namespace Auth.Application.DTOs;

/// <summary>
/// One role an organization's role pickers may offer: a membership role for an
/// invitation or a role change, or an application role for an assignment.
/// </summary>
public class OrganizationRoleOptionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
