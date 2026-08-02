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
    bool HealthChecksExposeErrorDetails);

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
            HealthChecksExposeErrorDetails: configuration.GetValue("HealthChecks:ExposeErrorDetails", false));
    }

    /// <summary>The values every consumer reads, at the moment it reads them.</summary>
    public GatewayRuntimeSettings Current => _current;

    /// <summary>Replaces the current values after a successful fetch.</summary>
    public void Update(GatewayRuntimeSettings settings) => _current = settings;
}
