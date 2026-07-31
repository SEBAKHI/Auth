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
}
