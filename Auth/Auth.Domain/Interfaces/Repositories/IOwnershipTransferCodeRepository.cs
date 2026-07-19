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
    /// Marks a code as used.
    /// </summary>
    /// <param name="codeId">The code ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkAsUsedAsync(Guid codeId, CancellationToken cancellationToken);

    /// <summary>
    /// Increments the attempt count for a code.
    /// </summary>
    /// <param name="codeId">The code ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task IncrementAttemptCountAsync(Guid codeId, CancellationToken cancellationToken);

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
