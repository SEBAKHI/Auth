using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetAssignableAppRoles;

/// <summary>
/// Query for the roles of one application that the caller may assign to a member of
/// this organization, or name in an invitation.
/// </summary>
/// <remarks>
/// Its own query rather than the platform-wide application role list, which is guarded
/// by <c>applications:read</c>, a platform permission an organization owner does not
/// hold. It also answers the question that list never asked: of the application's
/// roles, which ones would the assignment accept from this caller. A role carrying a
/// permission the caller does not itself hold is refused on save, so offering it would
/// lead to a refusal the caller cannot act on.
/// </remarks>
public record GetAssignableAppRolesQuery(
    Guid OrganizationId,
    Guid ApplicationId) : IRequest<ErrorOr<IReadOnlyList<OrganizationRoleOptionDto>>>
{
    /// <summary>The caller.</summary>
    public Guid RequestedBy { get; init; }

    /// <summary>
    /// Whether the caller's token carries platform permissions. The same flag the
    /// assignment passes to the grant check, so both see the same authority.
    /// </summary>
    public bool PlatformAuthorityInToken { get; init; }
}
