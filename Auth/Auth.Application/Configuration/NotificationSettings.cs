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
    /// is synchronous within the request.
    ///
    /// Now defaults true. Synchronous delivery puts SMTP connect-and-send —
    /// unbounded against a hung mail host — inside the request that triggered
    /// it, and a delivery failure surfaces as a failure of that request. That
    /// was tolerable while every send followed an explicit user action; it is
    /// not once a send hangs off signing in. The dispatcher retries with
    /// backoff and reclaims rows orphaned by an app-pool recycle.
    /// </summary>
    public bool UseOutbox { get; set; } = true;

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

    /// <summary>
    /// Whether a sign-in from an unrecognised device emails the account owner.
    /// </summary>
    public bool NewDeviceAlertEnabled { get; set; } = true;

    /// <summary>
    /// Shortest gap between two alerts for the same device. A genuinely new
    /// device is new exactly once, so this only collapses a burst of concurrent
    /// first sign-ins into a single email.
    /// </summary>
    public int NewDeviceAlertMinIntervalMinutes { get; set; } = 60;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);
    public TimeSpan StaleClaimAge => TimeSpan.FromMinutes(StaleClaimMinutes);
    public TimeSpan NewDeviceAlertMinInterval =>
        TimeSpan.FromMinutes(NewDeviceAlertMinIntervalMinutes);
}
