using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for user operations.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Gets a user by their unique identifier.
    /// </summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the users matching the given identifiers in a single round-trip.
    /// </summary>
    /// <remarks>
    /// Intended for display-name resolution of audit fields, so soft-deleted
    /// users are included — historical records keep resolving to a name.
    /// Missing identifiers are simply absent from the result.
    /// </remarks>
    Task<IReadOnlyList<User>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a user by their email address.
    /// </summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if an email address is reserved (used by any user, including soft-deleted users).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method intentionally includes soft-deleted users to enforce the email reservation policy.
    /// Once an email is used, it remains reserved forever and cannot be reused by new accounts.
    /// </para>
    /// <para>
    /// This follows enterprise identity management best practices (Microsoft, Google) to prevent:
    /// <list type="bullet">
    ///   <item><description>Identity confusion (new user receiving old user's communications)</description></item>
    ///   <item><description>Audit trail corruption (same email mapping to different users over time)</description></item>
    ///   <item><description>Legal/compliance issues with eDiscovery and data retention</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Note: This differs from <see cref="GetByEmailAsync"/> which only returns active users.
    /// </para>
    /// </remarks>
    /// <param name="email">The email address to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the email is reserved (used by any user); otherwise, <c>false</c>.</returns>
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new user.
    /// </summary>
    Task<User> CreateAsync(User user, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing user.
    /// </summary>
    Task UpdateAsync(User user, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a user by their identifier.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets users with pagination. <paramref name="sortBy"/> accepts the
    /// allow-listed field names in <see cref="Constants.SortFields.Users"/>;
    /// null keeps the default order.
    /// </summary>
    Task<(IReadOnlyList<User> Users, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? searchTerm,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a successful login for a user.
    /// </summary>
    Task RecordSuccessfulLoginAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Records a failed login attempt for a user.
    /// </summary>
    Task RecordFailedLoginAsync(Guid userId, int maxAttempts, TimeSpan lockoutDuration, CancellationToken cancellationToken);

    /// <summary>
    /// Unlocks a user account.
    /// </summary>
    Task UnlockAsync(Guid userId, Guid modifiedBy, CancellationToken cancellationToken);

    /// <summary>
    /// Updates a user's password.
    /// </summary>
    Task UpdatePasswordAsync(Guid userId, string passwordHash, Guid modifiedBy, CancellationToken cancellationToken);

    /// <summary>
    /// Confirms a user's email address.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="modifiedBy">Who confirmed the email.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConfirmEmailAsync(Guid userId, Guid modifiedBy, CancellationToken cancellationToken);

    #region User Roles

    /// <summary>
    /// Gets all role assignments for a user.
    /// </summary>
    Task<IReadOnlyList<UserRole>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a specific role assignment for a user.
    /// </summary>
    Task<UserRole?> GetUserRoleAsync(Guid userId, Guid roleId, Guid? applicationId, CancellationToken cancellationToken);

    /// <summary>
    /// Assigns a role to a user.
    /// </summary>
    Task<UserRole> AssignRoleAsync(UserRole userRole, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a role assignment from a user.
    /// </summary>
    Task RemoveRoleAsync(Guid userId, Guid roleId, Guid? applicationId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a user has a specific role.
    /// </summary>
    Task<bool> HasRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken);

    #endregion

    #region User Permissions (Direct Grants)

    /// <summary>
    /// Gets all direct permission grants for a user.
    /// </summary>
    Task<IReadOnlyList<UserPermission>> GetUserPermissionsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a specific permission grant for a user.
    /// </summary>
    Task<UserPermission?> GetUserPermissionAsync(Guid userId, Guid permissionId, Guid? applicationId, CancellationToken cancellationToken);

    /// <summary>
    /// Grants a permission directly to a user.
    /// </summary>
    Task<UserPermission> GrantPermissionAsync(UserPermission userPermission, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes a direct permission from a user.
    /// </summary>
    Task RevokePermissionAsync(Guid userId, Guid permissionId, Guid? applicationId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a user has a specific direct permission grant.
    /// </summary>
    Task<bool> HasDirectPermissionAsync(Guid userId, Guid permissionId, CancellationToken cancellationToken);

    #endregion

    /// <summary>
    /// Gets the distinct applications a user can access, either through an
    /// organization (membership + enabled app + app-level role or permission)
    /// or through a direct app-scoped role assignment.
    /// </summary>
    Task<IReadOnlyList<ReadModels.Access.UserApplicationAccess>> GetUserApplicationsAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
