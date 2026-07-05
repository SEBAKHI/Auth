using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetMemberAppRoles;

/// <summary>
/// Query to get all app-level role assignments for a member within an organization.
/// </summary>
public record GetMemberAppRolesQuery(
    Guid OrganizationId,
    Guid UserId) : IRequest<ErrorOr<IReadOnlyList<OrganizationMemberAppRoleDto>>>
{
    /// <summary>
    /// The user making the request (for authorization).
    /// </summary>
    public Guid RequestedBy { get; init; }
}
