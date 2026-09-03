using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for password reset token operations.
/// </summary>
public interface IPasswordResetTokenRepository
{
    /// <summary>
    /// Gets an unused, unexpired password reset token by its hash.
    /// </summary>
    /// <param name="tokenHash">The HMAC-SHA256 hash of the token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The token if found and valid, null otherwise.</returns>
    Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new password reset token.
    /// </summary>
    /// <param name="token">The token to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(PasswordResetToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a token as used.
    /// </summary>
    /// <param name="tokenId">The token ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkAsUsedAsync(Guid tokenId, CancellationToken cancellationToken);

    /// <summary>
    /// Invalidates all unused tokens for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the user currently holds a live token: unused and not yet
    /// expired. Issuance asks this first — one live link per account, and a
    /// request that finds one reissues nothing.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasLiveTokenAsync(Guid userId, CancellationToken cancellationToken);

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
