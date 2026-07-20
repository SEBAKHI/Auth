using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ReadModels.Notifications;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for the notification outbox: enqueue, claim-based
/// dispatch (safe under concurrent pollers and crashes), and status updates.
/// </summary>
public interface INotificationOutboxRepository
{
    /// <summary>
    /// Inserts a pending message.
    /// </summary>
    Task EnqueueAsync(NotificationOutboxMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> due Pending/Retry
    /// messages (marks them Processing) and returns them. Uses READPAST
    /// semantics so concurrent claimers never block or double-claim.
    /// </summary>
    Task<IReadOnlyList<NotificationOutboxMessage>> ClaimBatchAsync(
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a claimed message as sent.
    /// </summary>
    Task MarkSentAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Records a failed attempt: increments the attempt count and schedules the
    /// retry, or dead-letters the message once <paramref name="maxAttempts"/>
    /// is reached.
    /// </summary>
    Task MarkFailedAsync(
        Guid id,
        string error,
        DateTime nextAttemptAt,
        int maxAttempts,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns stale Processing rows (claimed before <paramref name="claimedBefore"/> —
    /// an app-pool recycle died mid-send) to Pending for immediate re-dispatch.
    /// Returns the number of reclaimed rows.
    /// </summary>
    Task<int> ReclaimStaleAsync(DateTime claimedBefore, CancellationToken cancellationToken);

    /// <summary>
    /// Whether any due Pending/Retry work exists (startup catch-up probe).
    /// </summary>
    Task<bool> HasDueWorkAsync(CancellationToken cancellationToken);

    #region Admin delivery log

    /// <summary>
    /// Gets a paginated delivery-log page (without body columns), with optional
    /// status/channel filters and recipient/type search.
    /// </summary>
    Task<(IReadOnlyList<NotificationOutboxListItem> Messages, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        NotificationDeliveryStatus? status,
        NotificationChannelType? channel,
        string? searchTerm,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets one outbox message with its full rendered bodies.
    /// </summary>
    Task<NotificationOutboxMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets delivery-log counts by status for the overview screen.
    /// </summary>
    Task<NotificationOutboxStats> GetStatsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Requeues a failed (Retry/Dead) message for immediate dispatch. Returns
    /// false when the message is not in a retryable status.
    /// </summary>
    Task<bool> RequeueAsync(Guid id, CancellationToken cancellationToken);

    #endregion
}
