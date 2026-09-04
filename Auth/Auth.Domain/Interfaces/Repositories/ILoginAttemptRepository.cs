using Auth.Domain.Entities;
using Auth.Domain.ReadModels.Authentication;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for login attempt operations.
/// </summary>
public interface ILoginAttemptRepository
{
    /// <summary>
    /// Records a login attempt.
    /// </summary>
    Task CreateAsync(LoginAttempt attempt, CancellationToken cancellationToken);

    /// <summary>
    /// Settles an open two-factor ceremony in place, so a sign-in that spanned two
    /// requests leaves one row rather than two. Only an open row is touched, which
    /// makes a repeated call a no-op and stops a settled outcome being rewritten.
    /// </summary>
    /// <param name="challengeId">The challenge whose ceremony row should be settled.</param>
    /// <param name="succeeded">True when the second factor was accepted.</param>
    /// <param name="failureReason">Why it failed; must be null when <paramref name="succeeded"/> is true.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ResolveTwoFactorCeremonyAsync(
        Guid challengeId,
        bool succeeded,
        string? failureReason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a user's recent sign-in ceremonies, newest first, each carrying the
    /// number of verification codes rejected during it.
    /// </summary>
    Task<IReadOnlyList<SignInHistoryEntry>> GetSignInHistoryAsync(
        Guid userId,
        int count,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets recent login attempts for an email.
    /// </summary>
    Task<IReadOnlyList<LoginAttempt>> GetRecentByEmailAsync(
        string email,
        int count,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets recent login attempts from an IP address.
    /// </summary>
    Task<IReadOnlyList<LoginAttempt>> GetRecentByIpAsync(
        string ipAddress,
        int count,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts failed attempts for an email within a time window.
    /// </summary>
    Task<int> CountFailedAttemptsAsync(
        string email,
        TimeSpan window,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts failed attempts from an IP within a time window.
    /// </summary>
    Task<int> CountFailedAttemptsByIpAsync(
        string ipAddress,
        TimeSpan window,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts failed attempts against one account from one client address
    /// within a time window. The per-source half of the lockout: an address
    /// that keeps failing is refused even when it is otherwise trusted.
    /// </summary>
    Task<int> CountFailedAttemptsForUserFromIpAsync(
        Guid userId,
        string ipAddress,
        TimeSpan window,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether this account has signed in successfully from the given client
    /// address within <paramref name="lookback"/>, or holds a session on the
    /// given device — a "familiar" source, which a lock raised by strangers'
    /// wrong passwords does not shut out.
    /// </summary>
    Task<bool> HasSucceededFromAsync(
        Guid userId,
        string? ipAddress,
        string? deviceId,
        TimeSpan lookback,
        CancellationToken cancellationToken);

    /// <summary>
    /// Cleans up old login attempts.
    /// </summary>
    Task CleanupOldAttemptsAsync(DateTime olderThan, CancellationToken cancellationToken);
}
