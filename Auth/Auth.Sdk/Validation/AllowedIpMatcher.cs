using System.Net;

namespace Auth.Sdk.Validation;

/// <summary>
/// Whether a caller's address is on an API key's address list (SDK 2.0).
/// </summary>
/// <remarks>
/// The address is the connection's: <c>HttpContext.Connection.RemoteIpAddress</c>, never a
/// forwarding header. Behind a proxy, configure ASP.NET Core's forwarded headers for your own
/// trusted proxy only, so the connection address is the caller's.
/// </remarks>
internal static class AllowedIpMatcher
{
    /// <summary>
    /// True when <paramref name="allowed"/> is empty (any address), or when
    /// <paramref name="remote"/>, unmapped from IPv4-mapped IPv6, equals one address or falls in
    /// one range. An unknown address matches nothing, and so does an entry that does not parse.
    /// </summary>
    public static bool IsAllowed(IReadOnlyList<string>? allowed, IPAddress? remote)
    {
        if (allowed is null || allowed.Count == 0)
        {
            return true;
        }

        if (remote is null)
        {
            return false;
        }

        var address = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;
        foreach (var entry in allowed)
        {
            if (entry.Contains('/'))
            {
                if (IPNetwork.TryParse(entry, out var network) && network.Contains(address))
                {
                    return true;
                }
            }
            else if (IPAddress.TryParse(entry, out var single) && single.Equals(address))
            {
                return true;
            }
        }

        return false;
    }
}
