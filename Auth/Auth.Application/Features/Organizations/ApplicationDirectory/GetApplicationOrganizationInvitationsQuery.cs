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
/// The <c>status</c> values as sent, each to be one exact status name; null or empty is
/// <see cref="DefaultStatuses"/>. Read by <see cref="ApplicationDirectoryPaging.ParseStatuses"/>
/// after the caller.
/// </param>
/// <param name="PageNumber">The query's value as sent, or null.</param>
/// <param name="PageSize">The query's value as sent, or null.</param>
public sealed record GetApplicationOrganizationInvitationsQuery(
    ApplicationDirectoryReader Caller,
    Guid OrganizationId,
    IReadOnlyCollection<string>? Statuses = null,
    string? PageNumber = null,
    string? PageSize = null)
    : IRequest<ErrorOr<ApplicationDirectoryPage<ApplicationOrganizationInvitationResponse>>>
{
    /// <summary>
    /// The statuses read when none is named: the invitations still open, and those that lapsed
    /// unanswered (an application's "resend" list).
    /// </summary>
    public static IReadOnlyCollection<InvitationStatus> DefaultStatuses { get; } =
        [InvitationStatus.Pending, InvitationStatus.Expired];
}
