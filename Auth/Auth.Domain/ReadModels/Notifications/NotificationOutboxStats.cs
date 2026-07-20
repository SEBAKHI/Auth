namespace Auth.Domain.ReadModels.Notifications;

/// <summary>
/// Aggregate counts over the delivery log, for the notifications overview.
/// Computed in SQL — the outbox grows without bound, so it is never counted by
/// pulling rows.
/// </summary>
/// <param name="Total">Every message ever enqueued.</param>
/// <param name="Pending">Waiting for a dispatch attempt (Pending or Processing).</param>
/// <param name="Sent">Delivered to the transport.</param>
/// <param name="Failed">Awaiting retry, or dead-lettered after the last attempt.</param>
/// <param name="Last24Hours">Enqueued within the trailing 24 hours.</param>
public sealed record NotificationOutboxStats(
    int Total,
    int Pending,
    int Sent,
    int Failed,
    int Last24Hours);
