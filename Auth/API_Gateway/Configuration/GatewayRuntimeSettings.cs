namespace API_Gateway.Configuration;

/// <summary>
/// The settings this process shares with the Auth API, as last known.
/// </summary>
/// <param name="Version">
/// The API's settings-layer version at the time these values were fetched.
/// <c>-1</c> means "never fetched" — the values are this process's own file
/// layer.
/// </param>
public sealed record GatewayRuntimeSettings(
    int Version,
    IReadOnlyList<string> CorsAllowedOrigins,
    bool CorsAllowCredentials,
    bool HealthChecksExposeErrorDetails,
    GatewayRateLimits RateLimits);

/// <summary>
/// The limiter numbers this process runs on.
/// <para>
/// Editable from the system-settings console even though the console cannot
/// see this process: the API serves them over the same pull that already
/// carries the CORS policy. A change lands within one poll interval, and
/// <c>Program.cs</c> stamps the settings version into every partition key so
/// the new limit applies to fresh partitions instead of waiting for open
/// windows to idle out.
/// </para>
/// </summary>
public sealed record GatewayRateLimits(
    int GlobalPermitLimit,
    int GlobalWindowSeconds,
    int GlobalQueueLimit,
    int AuthPermitLimit,
    int AuthWindowSeconds,
    int ApiPermitLimit,
    int ApiWindowSeconds,
    int AdminPermitLimit,
    int AdminWindowSeconds);

/// <summary>
/// Holds the current <see cref="GatewayRuntimeSettings"/> for every consumer in
/// this process.
/// <para>
/// Seeded from this process's own configuration file so the gateway is fully
/// functional before the first poll and whenever the API is unreachable —
/// fail-open to the file layer, mirroring DbSettingsConfigurationProvider on the
/// API side. The database being unavailable must never take the edge down.
/// </para>
/// </summary>
public sealed class GatewayRuntimeSettingsProvider
{
    private volatile GatewayRuntimeSettings _current;

    public GatewayRuntimeSettingsProvider(IConfiguration configuration)
    {
        _current = new GatewayRuntimeSettings(
            Version: -1,
            CorsAllowedOrigins: configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [],
            CorsAllowCredentials: configuration.GetValue("Cors:AllowCredentials", false),
            HealthChecksExposeErrorDetails: configuration.GetValue("HealthChecks:ExposeErrorDetails", false),
            // Seeded from THIS process's "RateLimiting" section — the API knows
            // the same values under "GatewayRateLimiting", named apart there
            // only so they cannot be confused with the API's own limits.
            RateLimits: ReadRateLimits(configuration));
    }

    /// <summary>The values every consumer reads, at the moment it reads them.</summary>
    public GatewayRuntimeSettings Current => _current;

    /// <summary>Replaces the current values after a successful fetch.</summary>
    public void Update(GatewayRuntimeSettings settings) => _current = settings;

    private static GatewayRateLimits ReadRateLimits(IConfiguration configuration) => new(
        GlobalPermitLimit: configuration.GetValue("RateLimiting:GlobalPermitLimit", 1000),
        GlobalWindowSeconds: configuration.GetValue("RateLimiting:GlobalWindowSeconds", 60),
        GlobalQueueLimit: configuration.GetValue("RateLimiting:GlobalQueueLimit", 100),
        AuthPermitLimit: configuration.GetValue("RateLimiting:AuthPermitLimit", 20),
        AuthWindowSeconds: configuration.GetValue("RateLimiting:AuthWindowSeconds", 60),
        ApiPermitLimit: configuration.GetValue("RateLimiting:ApiPermitLimit", 100),
        ApiWindowSeconds: configuration.GetValue("RateLimiting:ApiWindowSeconds", 60),
        AdminPermitLimit: configuration.GetValue("RateLimiting:AdminPermitLimit", 120),
        AdminWindowSeconds: configuration.GetValue("RateLimiting:AdminWindowSeconds", 60));
}
