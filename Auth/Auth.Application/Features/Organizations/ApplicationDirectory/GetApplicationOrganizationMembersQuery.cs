using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// One page of an organization's members, read by an application as one of its members
/// (OI-194). Requires <c>org:members:read</c> in the caller's membership; each member's roles in
/// the calling application are added when the membership also grants
/// <c>org:permissions:read</c>. Fixed order: joined first, then by membership id.
/// </summary>
public sealed record GetApplicationOrganizationMembersQuery(
    ApplicationDirectoryCaller Caller,
    Guid OrganizationId,
    int PageNumber = 1,
    int PageSize = ApplicationDirectoryPaging.DefaultPageSize)
    : IRequest<ErrorOr<ApplicationDirectoryPage<ApplicationOrganizationMemberResponse>>>;
