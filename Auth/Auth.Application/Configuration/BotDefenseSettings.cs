namespace Auth.Application.Configuration;

/// <summary>
/// Whether the bot check (S13) runs on the anonymous sign-in surfaces, and what it does with its
/// verdict.
/// </summary>
public enum BotDefenseMode
{
    /// <summary>No check: no widget is offered and no token is read. The shipped default.</summary>
    Off = 0,

    /// <summary>
    /// The widget is offered and a token that arrives is verified and logged, but nothing is
    /// refused. It protects nobody: it measures, before Enforce, how many real submissions pass.
    /// </summary>
    Monitor = 1,

    /// <summary>
    /// A request without a valid token is refused; when the check cannot run, the fail mode
    /// decides.
    /// </summary>
    Enforce = 2,
}

/// <summary>
/// What an enforced check does when it cannot run (Cloudflare unreachable, or the server's keys
/// not in place).
/// </summary>
public enum BotDefenseFailMode
{
    /// <summary>
    /// Per surface: a sign-up start is refused; a password sign-in, a code start and a reset
    /// request go through a small per-address allowance (RateLimiting:BotFallback*), then are
    /// refused. The shipped default.
    /// </summary>
    PerSurface = 0,

    /// <summary>Every protected request is refused while the check cannot run.</summary>
    Closed = 1,
}

/// <summary>
/// The bot check on the anonymous sign-in surfaces (S13): Cloudflare Turnstile. Every field is
/// read per request (IOptionsMonitor), so a change from the console applies without a restart;
/// the secret comes from the encrypted secrets file at startup.
/// </summary>
public sealed class BotDefenseSettings
{
    public const string SectionName = "BotDefense";

    /// <summary>The lowest <see cref="SiteverifyTimeoutMs"/> the console accepts and the client applies.</summary>
    public const int MinSiteverifyTimeoutMs = 500;

    /// <summary>The highest <see cref="SiteverifyTimeoutMs"/> the console accepts and the client applies.</summary>
    public const int MaxSiteverifyTimeoutMs = 5000;

    /// <summary>Off, Monitor or Enforce. Ships Off: no widget and no call.</summary>
    public BotDefenseMode Mode { get; set; } = BotDefenseMode.Off;

    /// <summary>What an enforced check does when it cannot run. Ships PerSurface.</summary>
    public BotDefenseFailMode FailMode { get; set; } = BotDefenseFailMode.PerSurface;

    /// <summary>
    /// The widget's public sitekey, which the apps read from <c>GET api/v1/Platform/bot-defense</c>.
    /// Empty means the check is not configured.
    /// </summary>
    public string SiteKey { get; set; } = string.Empty;

    /// <summary>
    /// The deadline of one verification, both attempts included, in milliseconds; clamped to
    /// [<see cref="MinSiteverifyTimeoutMs"/>, <see cref="MaxSiteverifyTimeoutMs"/>] where read.
    /// </summary>
    public int SiteverifyTimeoutMs { get; set; } = 2000;

    /// <summary>
    /// The widget's secret key, from the encrypted secrets file (<c>TurnstileSecretKey</c>),
    /// never from a settings file. Empty means the check is not configured.
    /// </summary>
    public string TurnstileSecret { get; set; } = string.Empty;

    /// <summary>The deadline as applied: <see cref="SiteverifyTimeoutMs"/> within its bounds.</summary>
    public TimeSpan SiteverifyTimeout =>
        TimeSpan.FromMilliseconds(Math.Clamp(SiteverifyTimeoutMs, MinSiteverifyTimeoutMs, MaxSiteverifyTimeoutMs));
}
