using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetPendingInvitations;

/// <summary>
/// Query to get pending invitations for an organization.
/// </summary>
public record GetPendingInvitationsQuery(
    Guid OrganizationId,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<OrganizationInvitationDto>>>
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
