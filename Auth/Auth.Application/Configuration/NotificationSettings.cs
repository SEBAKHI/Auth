namespace Auth.Application.Configuration;

/// <summary>
/// Configuration for the notification dispatch pipeline.
/// </summary>
public class NotificationSettings
{
    public const string SectionName = "Notifications";

    /// <summary>
    /// When true, sends are enqueued into the NotificationOutbox and delivered
    /// by the background dispatcher (retry with backoff); when false, delivery
    /// is synchronous within the request. Ships false and is flipped after soak.
    /// </summary>
    public bool UseOutbox { get; set; } = false;

    /// <summary>
    /// Fallback poll interval when no in-process enqueue signal arrives.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum messages claimed per dispatch cycle.
    /// </summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// Attempts before a message is dead-lettered.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Processing rows older than this are considered orphaned by a crashed
    /// worker (IIS recycle) and returned to Pending at startup and periodically.
    /// </summary>
    public int StaleClaimMinutes { get; set; } = 5;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);
    public TimeSpan StaleClaimAge => TimeSpan.FromMinutes(StaleClaimMinutes);
}
