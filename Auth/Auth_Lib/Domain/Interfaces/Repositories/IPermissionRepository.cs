using Auth_Lib.Domain.Entities;

namespace Auth_Lib.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for permission operations.
/// </summary>
public interface IPermissionRepository
{
    /// <summary>
    /// Gets a permission by its ID.
    /// </summary>
    Task<Permission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a permission by its code.
    /// </summary>
    Task<Permission?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all permissions for an application.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets permissions by hierarchy level.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetByLevelAsync(byte level, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets child permissions of a parent permission.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetChildPermissionsAsync(Guid parentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all effective permissions for a user (from roles, direct grants, and implications).
    /// </summary>
    Task<IReadOnlyList<string>> GetUserEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all effective permissions for a user within an application.
    /// </summary>
    Task<IReadOnlyList<string>> GetUserEffectivePermissionsAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user has a specific permission (considering wildcards and implications).
    /// </summary>
    Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a permission code exists.
    /// </summary>
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new permission.
    /// </summary>
    Task<Permission> CreateAsync(Permission permission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing permission.
    /// </summary>
    Task UpdateAsync(Permission permission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a permission.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants a permission to a role.
    /// </summary>
    Task GrantToRoleAsync(RolePermission rolePermission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a permission from a role.
    /// </summary>
    Task RevokeFromRoleAsync(Guid roleId, Guid permissionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants a permission directly to a user.
    /// </summary>
    Task GrantToUserAsync(UserPermission userPermission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a direct permission from a user.
    /// </summary>
    Task RevokeFromUserAsync(Guid userId, Guid permissionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets permissions assigned to a role.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken = default);

    #region Permission Implications

    /// <summary>
    /// Gets all implications for a permission (permissions that are implied when this permission is granted).
    /// </summary>
    Task<IReadOnlyList<PermissionImplication>> GetImplicationsAsync(Guid permissionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets permissions that imply the specified permission (reverse lookup).
    /// </summary>
    Task<IReadOnlyList<PermissionImplication>> GetImpliedByAsync(Guid permissionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a permission implication.
    /// </summary>
    Task<PermissionImplication> AddImplicationAsync(PermissionImplication implication, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a permission implication.
    /// </summary>
    Task RemoveImplicationAsync(Guid permissionId, Guid impliedPermissionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a permission implication exists.
    /// </summary>
    Task<bool> ImplicationExistsAsync(Guid permissionId, Guid impliedPermissionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if adding an implication would create a circular reference.
    /// </summary>
    Task<bool> WouldCreateCircularImplicationAsync(Guid permissionId, Guid impliedPermissionId, CancellationToken cancellationToken = default);

    #endregion

    #region Paginated Queries

    /// <summary>
    /// Gets permissions with pagination and optional filtering.
    /// </summary>
    Task<(IReadOnlyList<Permission> Permissions, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? applicationId = null,
        string? search = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default);

    #endregion
}
