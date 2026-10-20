using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// The one place that decides whether an application may read an organization's directory as
/// a user, and with which authority (OI-194, T4 to T6). The three directory queries ask it once
/// per request, before they read anything.
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
/// Every "not yours" is the same <see cref="OrganizationErrors.NotFound"/>: an organization that
/// does not exist, is inactive, did not enable the application (or let it lapse), or of which the
/// user is not an active member. A caller cannot tell one from another.
/// </para>
/// </remarks>
public sealed class ApplicationDirectoryAccess
{
    private readonly IUserRepository _userRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IOrganizationRepository _organizationRepository;

    public ApplicationDirectoryAccess(
        IUserRepository userRepository,
        IApplicationRepository applicationRepository,
        IOrganizationRepository organizationRepository)
    {
        _userRepository = userRepository;
        _applicationRepository = applicationRepository;
        _organizationRepository = organizationRepository;
    }

    /// <summary>
    /// Resolves the caller and the organization, and checks <paramref name="requiredPermission"/>
    /// against the caller's membership when one is named.
    /// </summary>
    /// <returns>
    /// The grant; or <see cref="ApplicationDirectoryRefusals.CallerNotServable"/>,
    /// <see cref="ApplicationDirectoryRefusals.InsufficientScope"/>,
    /// <see cref="OrganizationErrors.NotFound"/> or
    /// <see cref="OrganizationErrors.InsufficientPermissions"/>.
    /// </returns>
    public async Task<ErrorOr<ApplicationDirectoryGrant>> ResolveAsync(
        ApplicationDirectoryCaller caller,
        Guid organizationId,
        string? requiredPermission,
        CancellationToken cancellationToken)
    {
        if (caller.UserId == Guid.Empty || string.IsNullOrEmpty(caller.ApplicationCode))
        {
            return ApplicationDirectoryRefusals.CallerNotServable;
        }

        // Missing and soft-deleted rows are both null. The rule a refresh applies: an account
        // that may not renew credentials is not served by the token it still holds either.
        var user = await _userRepository.GetByIdAsync(caller.UserId, cancellationToken);
        if (user is null || !user.CanRenewCredentials())
        {
            return ApplicationDirectoryRefusals.CallerNotServable;
        }

        var application = await _applicationRepository.GetByCodeAsync(caller.ApplicationCode, cancellationToken);
        if (application is null || !application.IsActive)
        {
            return ApplicationDirectoryRefusals.CallerNotServable;
        }

        // Granted to the token AND still allowed to the application: an administrator's removal
        // takes effect at the next call, not at the token's next refresh.
        if (!ScopeSet.FromStored(caller.GrantedScope).Contains(OAuthScopes.OrganizationsRead)
            || !application.AllowedScopes.Contains(OAuthScopes.OrganizationsRead))
        {
            return ApplicationDirectoryRefusals.InsufficientScope;
        }

        var organization = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null
            || !organization.IsActive
            || !await _organizationRepository.IsApplicationEnabledAsync(organizationId, application.Id, cancellationToken))
        {
            return OrganizationErrors.NotFound(organizationId);
        }

        var membership = await _organizationRepository.GetMembershipAsync(organizationId, user.Id, cancellationToken);
        if (membership is null || !membership.IsValid())
        {
            return OrganizationErrors.NotFound(organizationId);
        }

        // The same query the permission gate falls back to: the membership role's org: codes,
        // live. Matched by the gate's wildcard rule (PermissionCode.Matches).
        var codes = await _organizationRepository.GetMembershipPermissionCodesAsync(organizationId, user.Id, cancellationToken);
        var grant = new ApplicationDirectoryGrant(organization, application, user.Id, codes);

        if (requiredPermission is not null && !grant.Holds(requiredPermission))
        {
            return OrganizationErrors.InsufficientPermissions;
        }

        return grant;
    }
}

/// <summary>
/// What <see cref="ApplicationDirectoryAccess"/> established for one request: the organization
/// (active, with the application enabled), the calling application, the member, and the codes
/// their membership grants in it now.
/// </summary>
public sealed record ApplicationDirectoryGrant(
    Organization Organization,
    AppEntity Application,
    Guid UserId,
    IReadOnlyList<string> MembershipPermissionCodes)
{
    /// <summary>
    /// Whether the membership grants <paramref name="permission"/>, by the permission gate's
    /// rule: <c>*</c>, an exact code, or a <c>prefix:*</c> wildcard over it.
    /// </summary>
    public bool Holds(string permission) =>
        MembershipPermissionCodes.Any(code => PermissionCode.From(code).Matches(permission));
}
