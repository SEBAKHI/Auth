using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GrantPermission;

/// <summary>
/// Command to grant an individual permission to a user within an organization.
/// </summary>
public record GrantPermissionCommand(
    Guid OrganizationId,
    Guid UserId,
    Guid ApplicationId,
    Guid PermissionId,
    DateTime? ExpiresAt = null) : IRequest<ErrorOr<OrganizationMemberPermissionDto>>
{
    /// <summary>
    /// The ID of the user granting the permission.
    /// </summary>
    public Guid GrantedBy { get; init; }

    /// <summary>
    /// Whether the actor's access token carries platform permissions, set by the
    /// controller. The live platform grants count toward what the actor may hand
    /// over only when it does (OrganizationGrantGuard).
    /// </summary>
    public bool PlatformAuthorityInToken { get; init; }
}
