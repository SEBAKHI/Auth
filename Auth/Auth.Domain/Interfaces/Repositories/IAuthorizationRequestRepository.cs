using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for pending OAuth authorize requests (OI-145).
/// </summary>
public interface IAuthorizationRequestRepository
{
    /// <summary>
    /// Stores a new pending request, first removing a few expired ones.
    /// </summary>
    /// <remarks>
    /// Every authorize without a session writes a row, so creation is bounded per
    /// address by the gateway but not in total; removing more expired rows than it
    /// adds keeps the table near the requests that can still resume, whatever the
    /// daily sweep's per-run ceiling.
    /// </remarks>
    Task CreateAsync(AuthorizationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the request whose handle is stored under one of the candidates, or null when there is none
    /// or it expired at or before <paramref name="nowUtc"/>.
    /// </summary>
    /// <remarks>
    /// The instant is passed in rather than read from the database server, so
    /// a resume and the sweep judge expiry against the same clock, the
    /// application's.
    /// </remarks>
    Task<AuthorizationRequest?> GetLiveByHandleHashAsync(
        CredentialHashCandidates candidates, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Persists <see cref="AuthorizationRequest.DropOrganizationRequest"/>: the
    /// request no longer asks for an organization.
    /// </summary>
    Task ClearCreateOrganizationAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes at most <paramref name="batchSize"/> requests that expired before
    /// <paramref name="olderThanUtc"/>, and reports how many went.
    /// </summary>
    /// <remarks>
    /// One bounded batch per call; the caller loops (see
    /// <see cref="IAuthorizationCodeRepository.CleanupExpiredAsync"/>).
    /// </remarks>
    /// <returns>Rows deleted; a value below <paramref name="batchSize"/> means the table is drained.</returns>
    Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken);
}
