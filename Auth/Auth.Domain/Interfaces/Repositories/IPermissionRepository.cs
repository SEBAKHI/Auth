using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;

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
    /// Lists every account that holds platform authority, each with the platform
    /// permission codes it holds — by the same scope rules as the platform overload
    /// of <see cref="GetUserEffectivePermissionsAsync(Guid, CancellationToken)"/>, so
    /// an application-scoped grant never counts. Accounts in any state are listed;
    /// the caller decides which of them may act.
    /// </summary>
    Task<IReadOnlyList<ReadModels.Access.PlatformAuthorityHolder>> GetUsersHoldingPlatformPermissionsAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// The platform overload of <see cref="GetUserEffectivePermissionsAsync(Guid, CancellationToken)"/>
    /// for several accounts in one read, by the same scope rules: every requested id
    /// is a key, with no codes when the account holds none.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetUsersEffectivePermissionsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>
    /// Every platform grant of the wildcard <c>*</c>, by the same scope rules as the
    /// platform overload of <see cref="GetUserEffectivePermissionsAsync(Guid, CancellationToken)"/>,
    /// each with its account's state as the same read saw it. Accounts in any state
    /// are listed; the caller decides which of them can sign in.
    /// </summary>
    Task<IReadOnlyList<FullAdministratorGrant>> GetUsersHoldingWildcardPermissionAsync(
        CancellationToken cancellationToken);

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
    /// Grants a permission directly to a user.
    /// </summary>
    Task GrantToUserAsync(UserPermission userPermission, CancellationToken cancellationToken);

    /// <summary>
    /// Gets permissions assigned to a role.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the permission codes of every role of an application, in one round trip:
    /// one (role, code) pair per active permission, with the same active-permission
    /// filter as <see cref="GetRolePermissionsAsync"/>, so a role's set here is exactly
    /// the set a single-role read returns. A role with no permissions has no pair.
    /// </summary>
    Task<IReadOnlyList<(Guid RoleId, string Code)>> GetApplicationRolePermissionCodesAsync(
        Guid applicationId,
        CancellationToken cancellationToken);

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

    /// <summary>
    /// Gets the distinct users granted the permission via a direct grant, an
    /// organization grant, or a role containing the permission, with pagination.
    /// <paramref name="sortBy"/> accepts the allow-listed field names in
    /// <see cref="Constants.SortFields.PermissionUsers"/>.
    /// </summary>
    Task<(IReadOnlyList<ReadModels.Access.PermissionUserRow> Users, int TotalCount)> GetUsersPagedAsync(
        Guid permissionId,
        int pageNumber,
        int pageSize,
        string? searchTerm,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    #endregion
}

/// <summary>
/// One way an account holds the platform wildcard <c>*</c>, with the account's state
/// and credentials as the same read saw them: a platform-scoped assignment of a role
/// that carries it (<see cref="RoleId"/> set), or a direct platform grant of it
/// (<see cref="RoleId"/> null). An account holding <c>*</c> both ways has one row per
/// way, so a removal can tell whether the account keeps <c>*</c> afterwards.
/// </summary>
/// <param name="UserId">The account.</param>
/// <param name="LockState">The account's status and lock columns.</param>
/// <param name="EmailConfirmed">Whether the account's email address is confirmed.</param>
/// <param name="IsDeleted">Whether the account is deleted or waiting for deletion.</param>
/// <param name="RoleId">The role that carries <c>*</c>; null for the direct grant.</param>
/// <param name="HasPassword">Whether the account has a password.</param>
/// <param name="HasExternalLogin">Whether the account has at least one linked external sign-in.</param>
/// <param name="ExpiresAt">When this way of holding <c>*</c> ends; null when it has no end date.</param>
public sealed record FullAdministratorGrant(
    Guid UserId,
    AccountLockState LockState,
    bool EmailConfirmed,
    bool IsDeleted,
    Guid? RoleId,
    bool HasPassword,
    bool HasExternalLogin,
    DateTime? ExpiresAt);
