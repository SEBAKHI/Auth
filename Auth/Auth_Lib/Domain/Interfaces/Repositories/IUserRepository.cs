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
    /// Checks if a user exists with the given email.
    /// </summary>
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
}
