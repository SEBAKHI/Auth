using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for OAuth authorization code operations.
/// </summary>
public interface IAuthorizationCodeRepository
{
    /// <summary>
    /// Creates a new authorization code.
    /// </summary>
    Task<AuthorizationCode> CreateAsync(AuthorizationCode code, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically marks the code with the given hash as consumed and returns it.
    /// Returns null when no unconsumed code with that hash exists — the caller
    /// must then treat the code as invalid (and may check
    /// <see cref="GetByCodeHashAsync"/> to detect a reuse attempt).
    /// </summary>
    Task<AuthorizationCode?> ConsumeByCodeHashAsync(string codeHash, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a code by its HMAC-SHA256 hash regardless of state (used for
    /// reuse-attempt detection after a failed consume).
    /// </summary>
    Task<AuthorizationCode?> GetByCodeHashAsync(string codeHash, CancellationToken cancellationToken);

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
