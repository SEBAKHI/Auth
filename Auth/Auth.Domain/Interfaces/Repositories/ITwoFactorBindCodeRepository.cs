using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Persistence for the codes emailed before an account binds its first second
/// factor.
/// </summary>
/// <remarks>
/// A code is consumed only by the transaction that switches the factor on
/// (<see cref="ITwoFactorStateStore.TryEnableAsync"/>), never on its own: a code
/// spent outside that transaction could be spent without binding anything.
/// </remarks>
public interface ITwoFactorBindCodeRepository
{
    /// <summary>
    /// Gets the account's newest code that is neither spent, superseded nor expired.
    /// </summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The live code, or null when there is none.</returns>
    Task<TwoFactorBindCode?> GetLiveForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a newly issued code.
    /// </summary>
    /// <param name="code">The code to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(TwoFactorBindCode code, CancellationToken cancellationToken);

    /// <summary>
    /// Reserves one verification attempt on a code before it is checked; the cap,
    /// the expiry and single use are conditions of the same statement.
    /// </summary>
    /// <param name="codeId">The code ID.</param>
    /// <param name="maxAttempts">The attempt allowance of a code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The attempt count including this reservation, or null when the code is
    /// spent, superseded, expired or out of attempts.
    /// </returns>
    Task<int?> TryReserveAttemptAsync(Guid codeId, int maxAttempts, CancellationToken cancellationToken);

    /// <summary>
    /// Closes every outstanding code of an account, so a fresh one supersedes them
    /// and a guesser never has more than one live target.
    /// </summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateOutstandingForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Counts the codes issued to an account inside a window, for the per-user
    /// issuance cap.
    /// </summary>
    /// <param name="userId">The account.</param>
    /// <param name="window">The window to count over.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of codes issued in the window.</returns>
    Task<int> GetRecentCountForUserAsync(Guid userId, TimeSpan window, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes codes that expired before a cutoff, one batch at a time, for the
    /// retention sweep.
    /// </summary>
    /// <param name="olderThanUtc">Codes that expired before this are removed.</param>
    /// <param name="batchSize">The most rows one call removes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows removed.</returns>
    Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken);
}
