using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetPendingInvitations;

/// <summary>
/// Query to get one page of an organization's invitations, narrowed by effective status.
/// </summary>
/// <param name="Statuses">
/// Effective statuses to return. Empty returns what the list always returned: every
/// invitation stored as pending, live or past its expiry.
/// </param>
public record GetPendingInvitationsQuery(
    Guid OrganizationId,
    IReadOnlyCollection<InvitationStatus>? Statuses = null,
    int PageNumber = 1,
    int PageSize = 20,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<PagedOrganizationInvitationsDto>>
{
    /// <summary>
    /// The ID of the user making the request.
    /// </summary>
    public Guid RequestedBy { get; set; }

    /// <summary>
    /// True when the caller holds the platform-wide organizations permission —
    /// skips the membership check. Set by the controller from JWT claims only,
    /// never bound from the request.
    /// </summary>
    public bool PlatformScope { get; init; }
}
