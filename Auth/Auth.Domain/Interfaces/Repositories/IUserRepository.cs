using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;

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
    /// The little a notification to an address's owner needs: who they are and
    /// what language they read. Deliberately not <see cref="GetByEmailAsync"/>:
    /// that one hydrates the whole aggregate and decrypts the phone number on
    /// the way, and it hides soft-deleted rows. This one reads three columns,
    /// decrypts nothing, and returns soft-deleted rows too — an address whose
    /// Users row still exists in any state cannot be registered again, so for
    /// a registration attempt it is "existing" whatever the row's status.
    /// </summary>
    /// <returns>The identity, or null when no Users row carries the address.</returns>
    Task<UserNotificationIdentity?> GetNotificationIdentityByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a user whose address was proved by a verify-first registration
    /// code, in ONE transaction with the proof: the pending row is locked, the
    /// code is checked against it once more (without charging an attempt —
    /// the attempt gate ran in the step before), the account row is inserted
    /// with the same statement every other door uses, and the pending row is
    /// stamped consumed. A code that no longer matches (the row was rotated
    /// or consumed under our feet) or a row that is gone or expired refuses
    /// the whole thing; a unique violation on the account row does the same
    /// and says so.
    /// </summary>
    /// <param name="user">The account to write, already confirmed.</param>
    /// <param name="pendingRegistrationId">The pending row the code was checked against.</param>
    /// <param name="otp">The code, presented again by the caller.</param>
    Task<VerifiedUserCreationOutcome> CreateVerifiedAsync(User user, Guid pendingRegistrationId, string otp, CancellationToken cancellationToken);

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

    // The lock columns (Status, FailedLoginAttempts, LockoutEndUtc) are written by
    // the five statements below and by the insert, and by nothing else: never by
    // UpdateAsync, whose entity may be minutes old. Each returns what it did, read
    // from its own OUTPUT clause, or null when no row matched.

    /// <summary>
    /// Records a completed sign-in: the last-login time and address. When
    /// <paramref name="passwordProven"/> is true the same statement also lifts an
    /// automatic lock (timed, hard or lapsed) and zeroes the failure count; an
    /// administrator's lock is never touched. When false, the lock columns are not
    /// written at all — a provider, an emailed code or a token exchange proves
    /// nothing about the password.
    /// </summary>
    Task<AccountLockTransition?> RecordSuccessfulLoginAsync(
        Guid userId, string? ipAddress, bool passwordProven, CancellationToken cancellationToken);

    /// <summary>
    /// Counts one wrong password, in one statement, in this precedence: reaching
    /// <paramref name="hardLimit"/> (or already hard) locks with no expiry; else
    /// every <paramref name="lockThreshold"/>-th consecutive failure locks for
    /// <paramref name="lockoutDuration"/>; else a lapsed lock is repaired to active.
    /// A null <paramref name="hardLimit"/> starts no new hard lock. Rows that are
    /// deleted, inactive, pending or under a live administrator's lock are not
    /// counted.
    /// </summary>
    Task<AccountLockTransition?> RecordFailedLoginAsync(
        Guid userId, int? hardLimit, int lockThreshold, TimeSpan lockoutDuration, CancellationToken cancellationToken);

    /// <summary>
    /// Lifts an automatic lock (timed, hard or lapsed) and zeroes the failure count,
    /// after a completed password reset or change. An administrator's lock, other
    /// statuses and the second factor's counter are not touched.
    /// </summary>
    Task<AccountLockTransition?> ClearAutomaticLockAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a lapsed lock (expiry passed) to active. The failure count is kept,
    /// so the next lock still counts toward the hard limit, unless
    /// <paramref name="resetFailureCount"/> is true (the legacy mode).
    /// </summary>
    Task<AccountLockTransition?> RepairLapsedLockAsync(
        Guid userId, bool resetFailureCount, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the three lock columns of an account that is not deleted, and nothing
    /// else: the cheap re-read a sign-in makes after its password hash, to judge
    /// the lock as it is now rather than as it was read. Null when the row is gone.
    /// </summary>
    Task<AccountLockState?> GetLockStateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes an administrator's status action: the status, and the count and
    /// expiry when the change resets them. Deleted accounts are not touched.
    /// </summary>
    Task SetAccountStatusAsync(
        Guid userId, AccountStatusChange change, Guid modifiedBy, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a change that can take away the last full administrator: takes the one
    /// lock every such change takes, then reads every platform grant of <c>*</c> under
    /// it. The lock is held until the change is committed or disposed.
    /// </summary>
    Task<IAccountAuthorityChange> BeginAuthorityChangeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Updates a user's password: a new password, so it stamps
    /// <c>LastPasswordChangeUtc</c> and clears <c>MustChangePassword</c>.
    /// </summary>
    Task UpdatePasswordAsync(Guid userId, string passwordHash, Guid modifiedBy, CancellationToken cancellationToken);

    /// <summary>
    /// Re-hashes the SAME password under new parameters (a sign-in found its hash
    /// stale). Not a password change: <c>LastPasswordChangeUtc</c> and
    /// <c>MustChangePassword</c> are left as they are, because the change stamp is
    /// what the administrator's second-factor reset gate trusts as the owner's own
    /// act (X12, LK4 (4)) — and a sign-in with a known password is anybody's act.
    /// Written only while the stored hash is still <paramref name="verifiedHash"/>:
    /// a password reset or change that landed while the sign-in hashed is kept, and
    /// the re-hash quietly changes nothing.
    /// </summary>
    /// <param name="verifiedHash">The stored hash the sign-in verified the password against.</param>
    /// <param name="passwordHash">The same password, hashed under the current parameters.</param>
    Task RehashPasswordAsync(Guid userId, string verifiedHash, string passwordHash, CancellationToken cancellationToken);

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
    /// Removes the one role assignment named by user, role and scope; a null
    /// <paramref name="applicationId"/> is the platform-wide assignment.
    /// </summary>
    /// <returns>Whether an assignment was removed; false when none matched.</returns>
    Task<bool> RemoveRoleAsync(Guid userId, Guid roleId, Guid? applicationId, CancellationToken cancellationToken);

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
    /// Revokes the one direct grant named by user, permission and scope; a null
    /// <paramref name="applicationId"/> is the platform-wide grant.
    /// </summary>
    /// <returns>Whether a grant was revoked; false when none matched.</returns>
    Task<bool> RevokePermissionAsync(Guid userId, Guid permissionId, Guid? applicationId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a user has a specific direct permission grant.
    /// </summary>
    Task<bool> HasDirectPermissionAsync(Guid userId, Guid permissionId, CancellationToken cancellationToken);

    #endregion
}

/// <summary>
/// What a message to an address's owner needs to know about them, and nothing
/// else. See <see cref="IUserRepository.GetNotificationIdentityByEmailAsync"/>.
/// </summary>
/// <param name="Id">The Users row.</param>
/// <param name="DisplayName">The stored full name; may be blank.</param>
/// <param name="PreferredLanguage">The language the owner reads notifications in.</param>
/// <param name="IsDeleted">Whether the row is soft-deleted. Informational: the caller treats every row as existing.</param>
public sealed record UserNotificationIdentity(
    Guid Id,
    string? DisplayName,
    string PreferredLanguage,
    bool IsDeleted);

/// <summary>
/// A change to accounts that can leave the platform without a full administrator —
/// an account holding <c>*</c> locked, deactivated or deleted, or a platform <c>*</c>
/// removed from one — made under one database lock that every such change takes,
/// from the read of <see cref="FullAdministratorGrants"/> to <see cref="Commit"/>.
/// Two of them therefore never decide on the same count: whichever runs second reads
/// what the first left.
/// </summary>
/// <remarks>
/// Disposing without <see cref="Commit"/> undoes every write made through it and
/// releases the lock. Nothing that waits on someone else — a publish, an email —
/// belongs inside it.
/// </remarks>
public interface IAccountAuthorityChange : IAsyncDisposable
{
    /// <summary>Every platform grant of <c>*</c>, read under the lock.</summary>
    IReadOnlyList<FullAdministratorGrant> FullAdministratorGrants { get; }

    /// <inheritdoc cref="IUserRepository.RemoveRoleAsync"/>
    Task<bool> RemoveRoleAsync(Guid userId, Guid roleId, Guid? applicationId, CancellationToken cancellationToken);

    /// <inheritdoc cref="IUserRepository.RevokePermissionAsync"/>
    Task<bool> RevokePermissionAsync(Guid userId, Guid permissionId, Guid? applicationId, CancellationToken cancellationToken);

    /// <inheritdoc cref="IUserRepository.SetAccountStatusAsync"/>
    Task SetAccountStatusAsync(
        Guid userId, AccountStatusChange change, Guid modifiedBy, CancellationToken cancellationToken);

    /// <inheritdoc cref="IUserRepository.DeleteAsync"/>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Makes every write final and releases the lock.</summary>
    void Commit();
}

/// <summary>
/// How <see cref="IUserRepository.CreateVerifiedAsync"/> ended.
/// </summary>
public enum VerifiedUserCreationOutcome
{
    /// <summary>The account row exists and the pending row is consumed.</summary>
    Created,

    /// <summary>
    /// No live pending row matched the code under the lock: gone, expired,
    /// rotated, or consumed since the caller's check. Nothing was written.
    /// </summary>
    CodeRejected,

    /// <summary>
    /// The account row collided with an existing one on the address. Nothing
    /// was written and the pending row is left as it was.
    /// </summary>
    DuplicateEmail
}
