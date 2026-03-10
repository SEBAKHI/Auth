using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for login attempt operations.
/// </summary>
public interface ILoginAttemptRepository
{
    /// <summary>
    /// Records a login attempt.
    /// </summary>
    Task CreateAsync(LoginAttempt attempt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent login attempts for a user.
    /// </summary>
    Task<IReadOnlyList<LoginAttempt>> GetRecentByUserAsync(
        Guid userId,
        int count,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent login attempts for an email.
    /// </summary>
    Task<IReadOnlyList<LoginAttempt>> GetRecentByEmailAsync(
        string email,
        int count,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent login attempts from an IP address.
    /// </summary>
    Task<IReadOnlyList<LoginAttempt>> GetRecentByIpAsync(
        string ipAddress,
        int count,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts failed attempts for an email within a time window.
    /// </summary>
    Task<int> CountFailedAttemptsAsync(
        string email,
        TimeSpan window,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts failed attempts from an IP within a time window.
    /// </summary>
    Task<int> CountFailedAttemptsByIpAsync(
        string ipAddress,
        TimeSpan window,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up old login attempts.
    /// </summary>
    Task CleanupOldAttemptsAsync(DateTime olderThan, CancellationToken cancellationToken = default);
}
