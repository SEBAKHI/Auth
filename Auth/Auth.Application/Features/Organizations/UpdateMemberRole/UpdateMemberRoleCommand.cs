using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.UpdateMemberRole;

/// <summary>
/// Command to update a member's organization role.
/// </summary>
public record UpdateMemberRoleCommand(
    Guid OrganizationId,
    Guid UserId,
    Guid NewRoleId) : IRequest<ErrorOr<OrganizationMemberDto>>
{
    /// <summary>
    /// The ID of the user performing the update.
    /// </summary>
    public Guid ModifiedBy { get; set; }
}
