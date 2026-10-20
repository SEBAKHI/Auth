using Auth.Domain.Entities;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// One page of the invitations that carry the calling application's role in an organization,
/// read by an application as one of its members (OI-194). Requires <c>org:members:invite</c>, as
/// the organization's own invitation list does. Fixed order: newest first, then by id.
/// </summary>
/// <param name="Statuses">
/// Effective statuses to return; null or empty is <see cref="DefaultStatuses"/>.
/// </param>
public sealed record GetApplicationOrganizationInvitationsQuery(
    ApplicationDirectoryCaller Caller,
    Guid OrganizationId,
    IReadOnlyCollection<InvitationStatus>? Statuses = null,
    int PageNumber = 1,
    int PageSize = ApplicationDirectoryPaging.DefaultPageSize)
    : IRequest<ErrorOr<ApplicationDirectoryPage<ApplicationOrganizationInvitationResponse>>>
{
    /// <summary>
    /// The statuses read when none is named: the invitations still open, and those that lapsed
    /// unanswered (an application's "resend" list).
    /// </summary>
    public static IReadOnlyCollection<InvitationStatus> DefaultStatuses { get; } =
        [InvitationStatus.Pending, InvitationStatus.Expired];
}
