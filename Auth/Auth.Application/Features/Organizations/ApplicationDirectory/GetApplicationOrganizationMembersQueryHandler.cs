using Auth.Application.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Organizations;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>Handles <see cref="GetApplicationOrganizationMembersQuery"/>.</summary>
/// <remarks>
/// Every active member is listed, with or without a role in the calling application: it is what
/// the same caller sees in the console, and the organization enabled the application. A page
/// costs two queries (members with their user and membership role, then the page's application
/// roles), whatever its size.
/// </remarks>
public sealed class GetApplicationOrganizationMembersQueryHandler
    : IRequestHandler<GetApplicationOrganizationMembersQuery, ErrorOr<ApplicationDirectoryPage<ApplicationOrganizationMemberResponse>>>
{
    private readonly ApplicationDirectoryAccess _access;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetApplicationOrganizationMembersQueryHandler> _logger;

    public GetApplicationOrganizationMembersQueryHandler(
        ApplicationDirectoryAccess access,
        IOrganizationRepository organizationRepository,
        IImageUrlComposer imageUrlComposer,
        TimeProvider timeProvider,
        ILogger<GetApplicationOrganizationMembersQueryHandler> logger)
    {
        _access = access;
        _organizationRepository = organizationRepository;
        _imageUrlComposer = imageUrlComposer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ErrorOr<ApplicationDirectoryPage<ApplicationOrganizationMemberResponse>>> Handle(
        GetApplicationOrganizationMembersQuery request,
        CancellationToken cancellationToken)
    {
        var grant = await _access.ResolveAsync(
            request.Caller, request.OrganizationId, PermissionCodes.Org.MembersRead, cancellationToken);
        if (grant.IsError)
        {
            return grant.Errors;
        }

        // One clock reading for the page: which memberships and role assignments have expired.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var (members, totalCount) = await _organizationRepository.GetActiveMembersPagedAsync(
            request.OrganizationId, now, request.PageNumber, request.PageSize, cancellationToken);

        // Role assignments are the console's org:permissions:read to show; without it the field
        // is absent rather than empty, so "no roles" is never claimed for a member who has some.
        var applicationRoles = grant.Value.Holds(PermissionCodes.Org.PermissionsRead)
            ? await ApplicationRolesAsync(grant.Value, members, now, cancellationToken)
            : null;

        var items = members
            .Select(member => new ApplicationOrganizationMemberResponse
            {
                UserId = member.UserId,
                Name = NameOf(member),
                Email = member.Email,
                PictureUrl = _imageUrlComposer.ComposeAbsolute(member.ProfileImageUrl),
                MembershipRole = member.MembershipRoleCode,
                JoinedAt = member.JoinedAt,
                ApplicationRoles = applicationRoles is null
                    ? null
                    : applicationRoles.GetValueOrDefault(member.UserId) ?? []
            })
            .ToList();

        ApplicationDirectoryLog.Read(_logger, grant.Value, "members", items.Count);

        return new ApplicationDirectoryPage<ApplicationOrganizationMemberResponse>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ApplicationRolesAsync(
        ApplicationDirectoryGrant grant,
        IReadOnlyList<ApplicationDirectoryMemberRow> members,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var rows = await _organizationRepository.GetMemberApplicationRolesAsync(
            grant.Organization.Id,
            grant.Application.Id,
            members.Select(member => member.UserId).ToList(),
            now,
            cancellationToken);

        return rows
            .GroupBy(row => row.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(row => row.RoleCode).Distinct(StringComparer.Ordinal).ToList());
    }

    private static string? NameOf(ApplicationDirectoryMemberRow member)
    {
        var name = $"{member.FirstName} {member.LastName}".Trim();
        return name.Length == 0 ? null : name;
    }
}
