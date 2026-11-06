using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// The WebAuthn challenges of the passkey ceremonies (S20, PK6). Each is spent by one
/// conditional statement whose affected-row count is the decision, so two requests that
/// carry the same handle can never both spend it.
/// </summary>
/// <remarks>
/// Every member answers as if the table were empty while the database has no
/// <c>WebAuthnChallenges</c> table (code deployed before its DACPAC): nothing is written,
/// nothing is found, nothing is swept.
/// </remarks>
public interface IWebAuthnChallengeRepository
{
    /// <summary>Stores a new challenge.</summary>
    Task CreateAsync(WebAuthnChallenge challenge, CancellationToken cancellationToken);

    /// <summary>
    /// Spends the account's registration challenge whose handle is one of
    /// <paramref name="handleCandidates"/>, in one statement: only an unused, unexpired
    /// registration challenge of <paramref name="userId"/> matches.
    /// </summary>
    /// <returns>The options it carried, or null when nothing matched.</returns>
    Task<string?> TryConsumeRegistrationAsync(
        CredentialHashCandidates handleCandidates,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Whether the account holds an unused, unexpired registration challenge.</summary>
    Task<bool> HasLiveRegistrationAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes challenges that expired before a cutoff, used or not, one batch at a time,
    /// for the retention sweep.
    /// </summary>
    /// <returns>The number of rows removed.</returns>
    Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken);
}
