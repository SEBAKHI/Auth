using Auth_Lib.Domain.Entities;

namespace Auth_Lib.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for user operations.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Gets a user by their unique identifier.
    /// </summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by their email address.
    /// </summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

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
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user.
    /// </summary>
    Task<User> CreateAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing user.
    /// </summary>
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user by their identifier.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets users with pagination.
    /// </summary>
    Task<(IReadOnlyList<User> Users, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? searchTerm = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a successful login for a user.
    /// </summary>
    Task RecordSuccessfulLoginAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed login attempt for a user.
    /// </summary>
    Task RecordFailedLoginAsync(Guid userId, int maxAttempts, TimeSpan lockoutDuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlocks a user account.
    /// </summary>
    Task UnlockAsync(Guid userId, Guid modifiedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a user's password.
    /// </summary>
    Task UpdatePasswordAsync(Guid userId, string passwordHash, Guid modifiedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms a user's email address.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="modifiedBy">Who confirmed the email.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConfirmEmailAsync(Guid userId, Guid modifiedBy, CancellationToken cancellationToken = default);
}
