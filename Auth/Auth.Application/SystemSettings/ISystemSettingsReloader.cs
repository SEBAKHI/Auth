namespace Auth.Application.SystemSettings;

/// <summary>
/// Reloads the database-backed configuration layer in-process after a save
/// or reset, firing the configuration change token so IOptionsMonitor /
/// IOptionsSnapshot consumers rebind (TemplateCache-style direct
/// invalidation; the periodic refresh service is only a safety net).
/// </summary>
public interface ISystemSettingsReloader
{
    /// <summary>
    /// Reloads overrides from the database and notifies configuration
    /// change-token listeners when anything actually changed.
    /// </summary>
    void Reload();

    /// <summary>
    /// Gets whether the most recent load attempt failed (database
    /// unreachable). The running configuration then still holds the last
    /// successfully loaded overrides (or none at startup) — surfaced to the
    /// console so an admin knows the view may be stale.
    /// </summary>
    bool LastLoadFailed { get; }

    /// <summary>
    /// Gets whether any load has succeeded since the process started. Unlike
    /// <see cref="LastLoadFailed"/>, a failed periodic refresh does not clear it:
    /// the configuration then still holds the values the last good load read.
    /// False only while the process has never read the overrides — it runs on the
    /// configuration files alone, which may disagree with what an administrator
    /// saved. A security switch that must not fail open reads this.
    /// </summary>
    bool HasLoadedSinceStart { get; }

    /// <summary>
    /// Monotonic counter bumped whenever a reload actually changed the
    /// loaded data. Consumers that cache derived state per key (the rate
    /// limiter's per-IP partitions) stamp this into their keys so changed
    /// limits take effect for new partitions immediately.
    /// </summary>
    int Version { get; }
}
