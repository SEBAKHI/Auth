using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// One page of an organization's members, read by an application as one of its members
/// (OI-194). Requires <c>org:members:read</c> in the caller's membership; each member's roles in
/// the calling application are added when the membership also grants
/// <c>org:permissions:read</c>. Fixed order: joined first, then by membership id.
/// </summary>
/// <param name="PageNumber">The query's value as sent, or null; read by <see cref="ApplicationDirectoryPaging.Parse"/> after the caller.</param>
/// <param name="PageSize">The query's value as sent, or null; read the same way.</param>
public sealed record GetApplicationOrganizationMembersQuery(
    ApplicationDirectoryCaller Caller,
    Guid OrganizationId,
    string? PageNumber = null,
    string? PageSize = null)
    : IRequest<ErrorOr<ApplicationDirectoryPage<ApplicationOrganizationMemberResponse>>>;
