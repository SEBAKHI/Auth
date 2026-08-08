namespace Auth.Application.Interfaces;

/// <summary>
/// Turns an IP address into an approximate place name for the session list.
///
/// "Approximate" is load-bearing and must reach the user: carrier NAT, VPNs and
/// corporate egress routinely put a sign-in hundreds of kilometres from where it
/// happened. Every vendor that shows a location says so, because the alternative
/// is a user reporting their own sign-in as an intrusion.
/// </summary>
public interface IGeoIpLookup
{
    /// <summary>
    /// Resolves a place name such as "Istanbul, Türkiye", or null when the
    /// address cannot be placed — unroutable, absent, or simply not in the
    /// database. Never throws: a sign-in must not depend on this.
    /// </summary>
    string? Resolve(string? ipAddress);
}
