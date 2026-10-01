using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for organization ownership transfer code operations.
/// </summary>
public interface IOwnershipTransferCodeRepository
{
    /// <summary>
    /// Gets the most recent unused, unexpired code for an organization.
    /// </summary>
    /// <param name="organizationId">The organization ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The most recent valid code if found, null otherwise.</returns>
    Task<OwnershipTransferCode?> GetValidForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new ownership transfer code.
    /// </summary>
    /// <param name="code">The code to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(OwnershipTransferCode code, CancellationToken cancellationToken);

    /// <summary>
    /// Reserves one verification attempt on a code before it is checked; the cap,
    /// the expiry and single use are conditions of the same statement.
    /// </summary>
    /// <param name="codeId">The code ID.</param>
    /// <param name="maxAttempts">The attempt allowance of a code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The attempt count including this reservation, or null when the code is
    /// used, expired or out of attempts.
    /// </returns>
    Task<int?> TryReserveAttemptAsync(Guid codeId, int maxAttempts, CancellationToken cancellationToken);

    /// <summary>
    /// Consumes a code that matched, giving back the attempt that matched.
    /// </summary>
    /// <param name="codeId">The code ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// True if this call consumed the code; false if it was already used, in
    /// which case the caller must act on nothing.
    /// </returns>
    Task<bool> TryConsumeAsync(Guid codeId, CancellationToken cancellationToken);

    /// <summary>
    /// Invalidates all unused codes for an organization.
    /// </summary>
    /// <param name="organizationId">The organization ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateAllForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the count of codes created for an organization in a time window (for rate limiting).
    /// </summary>
    /// <param name="organizationId">The organization ID.</param>
    /// <param name="window">The time window to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of codes created in the window.</returns>
    Task<int> GetRecentCountForOrganizationAsync(Guid organizationId, TimeSpan window, CancellationToken cancellationToken);
}
