using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Organizations.GrantPermission;

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
    public Guid GrantedBy { get; set; }
}
