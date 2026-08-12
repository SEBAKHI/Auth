using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for login-time two-factor challenge operations.
/// </summary>
public interface ITwoFactorChallengeRepository
{
    /// <summary>
    /// Gets a challenge by the hash of the presented token.
    /// </summary>
    /// <param name="tokenHash">The keyed hash of the challenge token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The challenge if found, null otherwise.</returns>
    Task<TwoFactorChallenge?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new two-factor challenge.
    /// </summary>
    /// <param name="challenge">The challenge to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(TwoFactorChallenge challenge, CancellationToken cancellationToken);

    /// <summary>
    /// Claims a challenge, marking it used. The claim is atomic: of two callers
    /// racing with the same still-valid code, exactly one is told it won.
    /// </summary>
    /// <param name="challengeId">The challenge ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// True if this call consumed the challenge; false if it was already used,
    /// in which case the caller must not issue anything.
    /// </returns>
    Task<bool> MarkAsUsedAsync(Guid challengeId, CancellationToken cancellationToken);

    /// <summary>
    /// Increments the attempt count for a challenge.
    /// </summary>
    /// <param name="challengeId">The challenge ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task IncrementAttemptCountAsync(Guid challengeId, CancellationToken cancellationToken);

    /// <summary>
    /// Invalidates all unused challenges for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Cleans up expired challenges older than 7 days.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CleanupExpiredAsync(CancellationToken cancellationToken);
}
