namespace Auth_Lib.Infrastructure.Configuration;

/// <summary>
/// Configuration settings for user sessions.
/// </summary>
public class SessionSettings
{
    public const string SectionName = "Session";

    /// <summary>
    /// Gets or sets the session lifetime in hours.
    /// </summary>
    public int LifetimeHours { get; set; } = 24;

    /// <summary>
    /// Gets or sets whether to extend session on activity.
    /// </summary>
    public bool ExtendOnActivity { get; set; } = true;

    /// <summary>
    /// Gets or sets the extension duration in hours when activity is detected.
    /// </summary>
    public int ExtensionHours { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum number of concurrent sessions per user.
    /// 0 = unlimited.
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 5;

    /// <summary>
    /// Gets or sets whether to terminate oldest session when max is reached.
    /// If false, new login will be rejected.
    /// </summary>
    public bool TerminateOldestOnMax { get; set; } = true;

    /// <summary>
    /// Gets or sets the idle timeout in minutes (0 = no idle timeout).
    /// </summary>
    public int IdleTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Gets or sets whether to terminate all sessions when a user changes their password.
    /// Default is true for security. Can be overridden per-request.
    /// </summary>
    public bool TerminateSessionsOnPasswordChange { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to terminate all sessions when a user resets their password.
    /// Default is true for security (account may be compromised). Can be overridden per-request.
    /// </summary>
    public bool TerminateSessionsOnPasswordReset { get; set; } = true;

    /// <summary>
    /// Gets the session lifetime as a TimeSpan.
    /// </summary>
    public TimeSpan Lifetime => TimeSpan.FromHours(LifetimeHours);

    /// <summary>
    /// Gets the extension duration as a TimeSpan.
    /// </summary>
    public TimeSpan ExtensionDuration => TimeSpan.FromHours(ExtensionHours);

    /// <summary>
    /// Gets the idle timeout as a TimeSpan.
    /// </summary>
    public TimeSpan IdleTimeout => TimeSpan.FromMinutes(IdleTimeoutMinutes);
}
