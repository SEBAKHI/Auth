using Auth.Domain.Interfaces.Repositories;

namespace Auth.Application.Common;

/// <summary>
/// Decides whether a role can serve as an application's organization creator
/// role: the facts checked when an administrator saves the setting, and again
/// every time the setting is used.
/// </summary>
/// <remarks>
/// Re-checked at use because the setting is not a foreign key and roles change
/// after it is saved: a role deactivated, emptied, deleted or moved to another
/// application makes organization creation unavailable. It never lets the step
/// grant a role the administrator did not choose, nor one that would carry no
/// organization claim at all — an empty role yields no <c>org_perm</c> pair and
/// so no <c>org_id</c>.
/// </remarks>
public class OrganizationCreatorRoleCheck
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;

    public OrganizationCreatorRoleCheck(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
    }

    /// <summary>
    /// Returns the role's active permission codes when the role belongs to the
    /// application, is active and carries at least one active permission.
    /// </summary>
    /// <returns>The codes, never empty; null when the role cannot be used.</returns>
    public async Task<IReadOnlyList<string>?> GetUsableCodesAsync(
        Guid applicationId,
        Guid? roleId,
        CancellationToken cancellationToken)
    {
        if (roleId is not Guid id || id == Guid.Empty)
        {
            return null;
        }

        var role = await _roleRepository.GetByIdAsync(id, cancellationToken);
        if (role is null || !role.IsActive || role.ApplicationId != applicationId)
        {
            return null;
        }

        var permissions = await _permissionRepository.GetRolePermissionsAsync(id, cancellationToken);
        var codes = permissions.Select(permission => permission.Code.Value).ToList();

        return codes.Count > 0 ? codes : null;
    }

    /// <summary>
    /// Returns the application's creator role when the application offers
    /// organization creation right now: its own settings
    /// (<see cref="Auth.Domain.Entities.Application.OffersOrganizationCreation"/>)
    /// and the role's facts.
    /// </summary>
    public async Task<Guid?> GetAvailableCreatorRoleAsync(
        Auth.Domain.Entities.Application application,
        CancellationToken cancellationToken)
    {
        if (!application.OffersOrganizationCreation)
        {
            return null;
        }

        var codes = await GetUsableCodesAsync(
            application.Id, application.OrganizationCreatorRoleId, cancellationToken);

        return codes is null ? null : application.OrganizationCreatorRoleId;
    }
}
