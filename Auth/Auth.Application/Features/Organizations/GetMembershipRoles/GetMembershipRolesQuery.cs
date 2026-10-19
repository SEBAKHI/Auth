using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetMembershipRoles;

/// <summary>
/// Query for the membership roles an organization's invitation and role-change
/// pickers may offer.
/// </summary>
/// <remarks>
/// Its own query rather than a filter over the platform-wide role list, which is
/// guarded by <c>roles:read</c>: a platform permission an organization owner has no
/// reason to hold, and without which the invite dialog's role list came back empty
/// with no explanation, so no invitation could be sent.
/// </remarks>
public record GetMembershipRolesQuery(
    Guid OrganizationId) : IRequest<ErrorOr<IReadOnlyList<OrganizationRoleOptionDto>>>;
