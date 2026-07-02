using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for permission operations.
/// </summary>
public interface IPermissionRepository
{
    /// <summary>
    /// Gets a permission by its ID.
    /// </summary>
    Task<Permission?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a permission by its code.
    /// </summary>
    Task<Permission?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all active permissions. <paramref name="sortBy"/> accepts the
    /// allow-listed field names in <see cref="Constants.SortFields.Permissions"/>;
    /// null keeps the default order.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetAllAsync(
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all permissions for an application. <paramref name="sortBy"/> accepts
    /// the allow-listed field names in <see cref="Constants.SortFields.Permissions"/>.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetByApplicationAsync(
        Guid applicationId,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets permissions by hierarchy level.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetByLevelAsync(byte level, CancellationToken cancellationToken);

    /// <summary>
    /// Gets child permissions of a parent permission.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetChildPermissionsAsync(Guid parentId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all effective permissions for a user (from roles, direct grants, and implications).
    /// </summary>
    Task<IReadOnlyList<string>> GetUserEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all effective permissions for a user within an application.
    /// </summary>
    Task<IReadOnlyList<string>> GetUserEffectivePermissionsAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a user has a specific permission (considering wildcards and implications).
    /// </summary>
    Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a permission code exists.
    /// </summary>
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new permission.
    /// </summary>
    Task<Permission> CreateAsync(Permission permission, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing permission.
    /// </summary>
    Task UpdateAsync(Permission permission, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a permission.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Grants a permission to a role.
    /// </summary>
    Task GrantToRoleAsync(RolePermission rolePermission, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes a permission from a role.
    /// </summary>
    Task RevokeFromRoleAsync(Guid roleId, Guid permissionId, CancellationToken cancellationToken);

    /// <summary>
    /// Grants a permission directly to a user.
    /// </summary>
    Task GrantToUserAsync(UserPermission userPermission, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes a direct permission from a user.
    /// </summary>
    Task RevokeFromUserAsync(Guid userId, Guid permissionId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets permissions assigned to a role.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken);

    #region Permission Implications

    /// <summary>
    /// Gets all implications for a permission (permissions that are implied when this permission is granted).
    /// </summary>
    Task<IReadOnlyList<PermissionImplication>> GetImplicationsAsync(Guid permissionId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets permissions that imply the specified permission (reverse lookup).
    /// </summary>
    Task<IReadOnlyList<PermissionImplication>> GetImpliedByAsync(Guid permissionId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a permission implication.
    /// </summary>
    Task<PermissionImplication> AddImplicationAsync(PermissionImplication implication, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a permission implication.
    /// </summary>
    Task RemoveImplicationAsync(Guid permissionId, Guid impliedPermissionId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a permission implication exists.
    /// </summary>
    Task<bool> ImplicationExistsAsync(Guid permissionId, Guid impliedPermissionId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if adding an implication would create a circular reference.
    /// </summary>
    Task<bool> WouldCreateCircularImplicationAsync(Guid permissionId, Guid impliedPermissionId, CancellationToken cancellationToken);

    #endregion

    #region Paginated Queries

    /// <summary>
    /// Gets permissions with pagination and optional filtering.
    /// </summary>
    Task<(IReadOnlyList<Permission> Permissions, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? applicationId,
        string? search,
        bool? isActive,
        CancellationToken cancellationToken);

    #endregion
}
