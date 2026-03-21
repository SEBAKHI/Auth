using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for password history operations.
/// </summary>
public interface IPasswordHistoryRepository
{
    /// <summary>
    /// Adds a password to history.
    /// </summary>
    Task AddAsync(PasswordHistory history, CancellationToken cancellationToken);

    /// <summary>
    /// Gets recent password hashes for a user.
    /// </summary>
    Task<IReadOnlyList<string>> GetRecentHashesAsync(Guid userId, int count, CancellationToken cancellationToken);

    /// <summary>
    /// Cleans up old password history beyond the retention count.
    /// </summary>
    Task CleanupOldHistoryAsync(Guid userId, int keepCount, CancellationToken cancellationToken);
}
