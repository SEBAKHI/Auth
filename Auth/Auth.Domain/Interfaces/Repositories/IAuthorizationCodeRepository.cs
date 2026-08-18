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
    /// Records which session a successful exchange produced, so a later replay
    /// of the same code knows exactly what to revoke.
    /// </summary>
    /// <remarks>
    /// Nothing else in the schema links a code to what it minted, and RFC 6749
    /// 4.1.2 asks the server to revoke everything issued from a code the moment
    /// that code is presented a second time.
    /// </remarks>
    Task RecordIssuedSessionAsync(Guid codeId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes expired codes older than the specified date.
    /// </summary>
    Task CleanupExpiredAsync(DateTime olderThan, CancellationToken cancellationToken);
}
