using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;

namespace Auth.Application.Features.Authentication.Common;

/// <inheritdoc />
public class TokenClaimsResolver : ITokenClaimsResolver
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IOrganizationRepository _organizationRepository;

    public TokenClaimsResolver(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IPermissionChecker permissionChecker,
        IOrganizationRepository organizationRepository)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _permissionChecker = permissionChecker;
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

        // Through the permission checker rather than the repository directly: it
        // already unions the application-scoped direct grants with the ones the
        // user holds through organizations, and it was registered but injected
        // nowhere until this call site existed.
        var permissions = await _permissionChecker.GetUserPermissionsAsync(userId, appId, cancellationToken);

        var organizationPermissions = await _organizationRepository
            .GetMembershipPermissionCodesForApplicationAsync(userId, appId, cancellationToken);

        return new TokenClaims(
            roles.Select(r => r.Code).ToList(),
            permissions,
            organizationPermissions);
    }
}
