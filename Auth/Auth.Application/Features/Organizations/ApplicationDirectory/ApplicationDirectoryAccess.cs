using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Organizations;
using Auth.Domain.ValueObjects;
using ErrorOr;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// The one place that decides whether an application may read an organization's directory, and
/// with which authority: as a user (OI-194, T4 to T6) or by itself with its machine key (OI-202,
/// T6). The three directory queries ask it once per request, before they read anything.
/// </summary>
/// <remarks>
/// <para>
/// Everything comes from the database at the call. The token only says who is asking, through
/// which application and with which scopes. Its <c>permissions</c> claim holds the application's
/// own codes, which an application may name in a platform namespace (an unseeded
/// <c>organizations:*</c>), and its <c>org_perm</c> is a copy of the membership as it was at
/// sign-in: neither is read here.
/// </para>
/// <para>
/// Who is asking is two steps of the caller itself (<see cref="ApplicationDirectoryReader"/>):
/// which application, still allowed the scope; and what it may read in the organization. The
/// organization's rules sit here, once, for both: it exists, is active, and enabled the
/// application (its expiry included).
/// </para>
/// <para>
/// Every "not yours" is the same <see cref="OrganizationErrors.NotFound"/>: an organization that
/// does not exist, is inactive, did not enable the application (or let it lapse), or of which the
/// user is not an active member. A caller cannot tell one from another.
/// </para>
/// </remarks>
public sealed class ApplicationDirectoryAccess
{
    private readonly ApplicationDirectorySources _sources;

    public ApplicationDirectoryAccess(
        IUserRepository userRepository,
        IApplicationRepository applicationRepository,
        IOrganizationRepository organizationRepository)
    {
        _sources = new ApplicationDirectorySources(userRepository, applicationRepository, organizationRepository);
    }

    /// <summary>
    /// Resolves the caller and the organization, and checks <paramref name="requiredPermission"/>
    /// against the caller's authority when one is named.
    /// </summary>
    /// <returns>
    /// The grant; or <see cref="ApplicationDirectoryRefusals.CallerNotServable"/>,
    /// <see cref="ApplicationDirectoryRefusals.InsufficientScope"/>,
    /// <see cref="OrganizationErrors.NotFound"/> or
    /// <see cref="OrganizationErrors.InsufficientPermissions"/>.
    /// </returns>
    public async Task<ErrorOr<ApplicationDirectoryGrant>> ResolveAsync(
        ApplicationDirectoryReader caller,
        Guid organizationId,
        string? requiredPermission,
        CancellationToken cancellationToken)
    {
        var application = await caller.ResolveApplicationAsync(_sources, cancellationToken);
        if (application.IsError)
        {
            return application.Errors;
        }

        var organization = await _sources.Organizations.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null
            || !organization.IsActive
            || !await _sources.Organizations.IsApplicationEnabledAsync(organizationId, application.Value.Id, cancellationToken))
        {
            return OrganizationErrors.NotFound(organizationId);
        }

        var authority = await caller.AuthorityInAsync(_sources, organization, cancellationToken);
        if (authority.IsError)
        {
            return authority.Errors;
        }

        var grant = new ApplicationDirectoryGrant(organization, application.Value, authority.Value);
        if (requiredPermission is not null && !grant.Holds(requiredPermission))
        {
            return OrganizationErrors.InsufficientPermissions;
        }

        return grant;
    }
}

/// <summary>The repositories the caller's own steps read, handed to them by <see cref="ApplicationDirectoryAccess"/>.</summary>
internal sealed record ApplicationDirectorySources(
    IUserRepository Users,
    IApplicationRepository Applications,
    IOrganizationRepository Organizations);

/// <summary>
/// What <see cref="ApplicationDirectoryAccess"/> established for one request: the organization
/// (active, with the application enabled), the calling application, and what the caller may read
/// in it now.
/// </summary>
public sealed record ApplicationDirectoryGrant(
    Organization Organization,
    AppEntity Application,
    ApplicationDirectoryAuthority Authority)
{
    /// <summary>A member's grant: the codes their membership grants in the organization now.</summary>
    public ApplicationDirectoryGrant(
        Organization organization,
        AppEntity application,
        Guid userId,
        IReadOnlyList<string> membershipPermissionCodes)
        : this(organization, application, new ApplicationDirectoryMembershipAuthority(userId, membershipPermissionCodes))
    {
    }

    /// <summary>Whether the caller's authority grants <paramref name="permission"/>.</summary>
    public bool Holds(string permission) => Authority.Holds(permission);
}

/// <summary>
/// What a directory caller may read in an organization: a member's live codes
/// (<see cref="ApplicationDirectoryMembershipAuthority"/>), or an application's fixed machine
/// authority (<see cref="ApplicationDirectoryMachineAuthority"/>).
/// </summary>
public abstract record ApplicationDirectoryAuthority
{
    /// <summary>Who is reading, for the log: <c>user</c> or <c>machine</c>. Never a person's id.</summary>
    public abstract string CallerKind { get; }

    /// <summary>
    /// Whether the authority grants <paramref name="permission"/>, one of the organization codes
    /// the directory reads require.
    /// </summary>
    public abstract bool Holds(string permission);

    /// <summary>One page of the members this caller may list, with their total.</summary>
    internal abstract Task<(IReadOnlyList<ApplicationDirectoryMemberRow> Members, int TotalCount)> MembersPageAsync(
        IOrganizationRepository organizations,
        ApplicationDirectoryGrant grant,
        DateTime now,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);
}

/// <summary>
/// A member's authority (OI-194): the codes their membership grants in the organization now,
/// matched by the permission gate's rule: <c>*</c>, an exact code, or a <c>prefix:*</c> wildcard
/// over it. Lists every active member, with or without a role in the calling application.
/// </summary>
public sealed record ApplicationDirectoryMembershipAuthority(
    Guid UserId,
    IReadOnlyList<string> MembershipPermissionCodes) : ApplicationDirectoryAuthority
{
    /// <inheritdoc />
    public override string CallerKind => "user";

    /// <inheritdoc />
    public override bool Holds(string permission) =>
        MembershipPermissionCodes.Any(code => PermissionCode.From(code).Matches(permission));

    /// <inheritdoc />
    internal override Task<(IReadOnlyList<ApplicationDirectoryMemberRow> Members, int TotalCount)> MembersPageAsync(
        IOrganizationRepository organizations,
        ApplicationDirectoryGrant grant,
        DateTime now,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken) =>
        organizations.GetActiveMembersPagedAsync(grant.Organization.Id, now, pageNumber, pageSize, cancellationToken);
}

/// <summary>
/// An application's authority when it reads by itself (OI-202, D-202-3): fixed, since no person
/// vouches for the read. The organization; the members who hold one of this application's roles
/// in it, each with those roles; and the invitations that carry this application's role. Nothing
/// of the organization's other members, and no user id.
/// </summary>
public sealed record ApplicationDirectoryMachineAuthority : ApplicationDirectoryAuthority
{
    /// <summary>The one machine authority: it carries no state.</summary>
    public static readonly ApplicationDirectoryMachineAuthority Instance = new();

    /// <summary>The directory reads this authority opens, by the codes their queries require.</summary>
    private static readonly IReadOnlyList<string> DirectoryReads =
        [PermissionCodes.Org.MembersRead, PermissionCodes.Org.PermissionsRead, PermissionCodes.Org.MembersInvite];

    private ApplicationDirectoryMachineAuthority()
    {
    }

    /// <inheritdoc />
    public override string CallerKind => "machine";

    /// <inheritdoc />
    public override bool Holds(string permission) => DirectoryReads.Contains(permission, StringComparer.Ordinal);

    /// <inheritdoc />
    internal override Task<(IReadOnlyList<ApplicationDirectoryMemberRow> Members, int TotalCount)> MembersPageAsync(
        IOrganizationRepository organizations,
        ApplicationDirectoryGrant grant,
        DateTime now,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken) =>
        organizations.GetActiveMembersHoldingApplicationRolePagedAsync(
            grant.Organization.Id, grant.Application.Id, now, pageNumber, pageSize, cancellationToken);
}
