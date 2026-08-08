namespace Auth.Application.Configuration;

/// <summary>
/// Configuration for resolving an approximate place name from an IP address.
/// </summary>
public class GeoIpSettings
{
    public const string SectionName = "GeoIp";

    /// <summary>
    /// Whether to resolve locations at all. Off by default: the lookup needs a
    /// database file that is not part of the deployment, and a feature that
    /// silently produces nothing is worse than one that is visibly switched off.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Absolute or content-root-relative path to a MaxMind GeoLite2-City
    /// <c>.mmdb</c> file. The database is read from disk — there is deliberately
    /// no web-service mode, because that would put a third-party network call in
    /// the sign-in path and tell that third party who signs in from where.
    /// </summary>
    public string DatabasePath { get; set; } = string.Empty;
}
