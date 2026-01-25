using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.OrganizationManagement.Commands;

/// <summary>
/// Command to assign an app-level role to a user within an organization.
/// </summary>
public record AssignAppRoleCommand(
    Guid OrganizationId,
    Guid UserId,
    Guid ApplicationId,
    Guid RoleId,
    DateTime? ExpiresAt = null) : IRequest<ErrorOr<OrganizationMemberAppRoleDto>>
{
    /// <summary>
    /// The ID of the user assigning the role.
    /// </summary>
    public Guid AssignedBy { get; set; }
}
