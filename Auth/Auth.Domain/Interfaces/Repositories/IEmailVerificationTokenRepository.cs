using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for email verification token operations.
/// </summary>
public interface IEmailVerificationTokenRepository
{
    /// <summary>
    /// Gets the most recent valid token for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The most recent valid token if found, null otherwise.</returns>
    Task<EmailVerificationToken?> GetValidTokenForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new email verification token.
    /// </summary>
    /// <param name="token">The token to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(EmailVerificationToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a token as used.
    /// </summary>
    /// <param name="tokenId">The token ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkAsUsedAsync(Guid tokenId, CancellationToken cancellationToken);

    /// <summary>
    /// Increments the attempt count for a token.
    /// </summary>
    /// <param name="tokenId">The token ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task IncrementAttemptCountAsync(Guid tokenId, CancellationToken cancellationToken);

    /// <summary>
    /// Invalidates all unused tokens for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the count of tokens created for an email in a time window (for rate limiting).
    /// </summary>
    /// <param name="email">The email address.</param>
    /// <param name="window">The time window to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of tokens created in the window.</returns>
    Task<int> GetRecentTokenCountAsync(string email, TimeSpan window, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes at most <paramref name="batchSize"/> rows that fell out of use
    /// before <paramref name="olderThanUtc"/>, and reports how many went.
    /// </summary>
    /// <remarks>
    /// One bounded batch per call, deliberately. The caller loops, which keeps
    /// the row count of a single statement below the ~5000 locks at which SQL
    /// Server escalates to a table lock, and keeps each batch in its own implicit
    /// transaction so the log can be truncated between them. An unbounded DELETE
    /// over a table that accumulated for months would hold both.
    /// <para>
    /// The cutoff is passed in rather than computed here so every table is swept
    /// against one clock — the application's — instead of each statement reading
    /// the database server's.
    /// </para>
    /// </remarks>
    /// <returns>Rows deleted; a value below <paramref name="batchSize"/> means the table is drained.</returns>
    Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken);
}
