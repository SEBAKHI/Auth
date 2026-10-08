using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.AssignAppRole;

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
    public Guid AssignedBy { get; init; }

    /// <summary>
    /// Whether the actor's access token carries platform permissions, set by the
    /// controller. The live platform grants count toward what the actor may hand
    /// over only when it does (OrganizationGrantGuard).
    /// </summary>
    public bool PlatformAuthorityInToken { get; init; }
}
