using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for user operations.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Gets a user by their unique identifier. Soft-deleted users are excluded.
    /// </summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a user by their unique identifier, including soft-deleted users.
    /// Intended for administrative flows that operate on deleted accounts
    /// (permanent deletion); operational reads must use <see cref="GetByIdAsync"/>.
    /// </summary>
    Task<User?> GetByIdIncludeDeletedAsync(Guid id, CancellationToken cancellationToken);

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
    /// Gets the minimal recipient projection of every account eligible for a
    /// platform-wide notice: active, not soft-deleted, email confirmed.
    /// Intentionally a projection — bulk sends must not hydrate full entities
    /// (or decrypt phone numbers) for the whole user base.
    /// </summary>
    Task<IReadOnlyList<(Guid Id, string Email, string? DisplayName, string? FirstName, string? PreferredLanguage)>>
        GetActiveNotificationRecipientsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a user by their email address.
    /// </summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a user by their email address, including soft-deleted users.
    /// Intended for the deletion recovery flow and the pending-deletion login
    /// signal; operational reads must use <see cref="GetByEmailAsync"/>.
    /// </summary>
    Task<User?> GetByEmailIncludeDeletedAsync(string email, CancellationToken cancellationToken);

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
    /// Soft-deletes a user by their identifier (sets IsDeleted; the row and its
    /// history remain, and the email stays reserved).
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Clears the soft-delete flag (grace-period account recovery). Only the
    /// recovery flow may call this, after the deletion request was cancelled.
    /// </summary>
    Task RestoreAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Permanently removes a soft-deleted user and every dependent record
    /// (sessions, tokens, role/permission assignments, organization
    /// memberships, notifications, and the user's audit trail) in a single
    /// transaction. Actor references on records that belong to other entities
    /// are reattributed to the system account so no orphaned references remain.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the user was purged; <c>false</c> when no soft-deleted
    /// user with the given id exists (nothing was changed).
    /// </returns>
    /// <remarks>
    /// Staged destruction: writes the zero-PII tombstone (a PERMANENT
    /// identifier reservation — deleted emails/usernames are never recycled),
    /// crypto-shreds the per-user encryption key, anonymizes the audit and
    /// login-attempt history in place, cascades every owned row and finally
    /// removes the account row. Terminal AccountDeletionRequests rows are
    /// retained untouched as destruction evidence.
    /// </remarks>
    Task<bool> HardDeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets users with pagination. <paramref name="sortBy"/> accepts the
    /// allow-listed field names in <see cref="Constants.SortFields.Users"/>;
    /// null keeps the default order. <paramref name="includeDeleted"/> widens
    /// the result to soft-deleted users; callers must gate it behind user
    /// management permission.
    /// </summary>
    Task<(IReadOnlyList<User> Users, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? searchTerm,
        string? sortBy,
        Enums.SortDirection sortDirection,
        bool includeDeleted,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a successful login for a user.
    /// </summary>
    Task RecordSuccessfulLoginAsync(Guid userId, string? ipAddress, CancellationToken cancellationToken);

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
