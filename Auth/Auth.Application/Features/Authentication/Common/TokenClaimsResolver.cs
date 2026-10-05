using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;

namespace Auth.Application.Features.Authentication.Common;

/// <inheritdoc />
public class TokenClaimsResolver : ITokenClaimsResolver
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IOrganizationRepository _organizationRepository;

    public TokenClaimsResolver(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IOrganizationRepository organizationRepository)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _organizationRepository = organizationRepository;
    }

    /// <inheritdoc />
    public async Task<TokenClaims> ResolveAsync(
        Guid userId,
        Guid? applicationId,
        CancellationToken cancellationToken)
    {
        if (applicationId is not Guid appId)
        {
            // Platform token: the user's full authority, unchanged from before.
            var platformRoles = await _roleRepository.GetUserRolesAsync(userId, cancellationToken);
            var platformPermissions = await _permissionRepository.GetUserEffectivePermissionsAsync(
                userId, cancellationToken);
            var platformOrgPermissions = await _organizationRepository.GetMembershipPermissionCodesAsync(
                userId, cancellationToken);

            return new TokenClaims(
                platformRoles.Select(r => r.Code).ToList(),
                platformPermissions,
                platformOrgPermissions);
        }

        var roles = await _roleRepository.GetUserRolesForApplicationAsync(userId, appId, cancellationToken);

        // Application-scoped DIRECT grants only. This used to go through the
        // permission checker, which unions in everything the user holds through
        // their organizations - and unions it FLAT, with no record of which
        // organization granted what.
        //
        // That flattening was the defect. A user who belongs to organization A
        // (granted, say, invoices:delete there) and to organization B, both of
        // which enable this application, received one token carrying a bare
        // "invoices:delete". The relying party - using the SDK shipped in this
        // repository, which authorizes on exactly that claim - had no way to know
        // the grant was A's, so the permission was spendable on B's data. The
        // org_perm claims did not help: they were filtered to 'org:%' codes, so
        // the business permission had no scoped counterpart at all.
        //
        // Delegated permissions now ride ONLY in org_perm, tagged with the
        // organization that granted them, and the flat list carries only what is
        // genuinely application-wide for this user.
        var permissions = await _permissionRepository.GetUserEffectivePermissionsAsync(
            userId, appId, cancellationToken);

        // Two sources, both already organization-tagged: membership authority
        // ('org:%' codes, from the role bound to the membership) and delegated
        // authority (any code, granted per organization per application).
        var membershipPermissions = await _organizationRepository
            .GetMembershipPermissionCodesForApplicationAsync(userId, appId, cancellationToken);

        var delegatedPermissions = await _organizationRepository
            .GetEffectivePermissionPairsForApplicationAsync(userId, appId, cancellationToken);

        var organizationPermissions = membershipPermissions
            .Concat(delegatedPermissions)
            .Distinct()
            .ToList();

        return new TokenClaims(
            roles.Select(r => r.Code).ToList(),
            permissions,
            organizationPermissions,
            await ResolveSingleOrganizationAsync(delegatedPermissions, cancellationToken));
    }

    /// <summary>
    /// The organization to name in <c>org_id</c>/<c>org_name</c>: the one where
    /// the user holds this application's delegated codes, when there is exactly
    /// one. Membership authority (<c>org:</c> codes) does not count: every member
    /// of an organization that enabled the application has it, and it says
    /// nothing about the application. Two or more organizations name none, so a
    /// relying party never acts on a guess; it reads <c>org_perm</c> instead.
    /// </summary>
    private async Task<TokenOrganization?> ResolveSingleOrganizationAsync(
        IReadOnlyList<(Guid OrganizationId, string Code)> delegatedPermissions,
        CancellationToken cancellationToken)
    {
        var organizationIds = delegatedPermissions
            .Where(pair => !pair.Code.StartsWith("org:", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.OrganizationId)
            .Distinct()
            .Take(2)
            .ToList();

        if (organizationIds.Count != 1)
        {
            return null;
        }

        // The pairs query already required the organization to be active; a row
        // gone between the two reads names nothing rather than a stale id.
        var organization = await _organizationRepository.GetByIdAsync(organizationIds[0], cancellationToken);
        return organization is { IsActive: true }
            ? new TokenOrganization(organization.Id, organization.Name)
            : null;
    }
}
