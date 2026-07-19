namespace Auth_API.Common;

/// <summary>
/// Single source of truth for the caller's client IP. Behind the gateway the
/// TCP peer (Connection.RemoteIpAddress) is the internal host, identical for
/// every request (hairpin), so the real client address must be read from the
/// X-Forwarded-For header the gateway sets. Used for both audit logging and
/// per-client rate-limit partitioning so the two never diverge.
/// </summary>
public static class ClientIpResolver
{
    /// <summary>
    /// Resolves the client IP: the first X-Forwarded-For entry when present,
    /// otherwise the direct connection address (proxy-less dev).
    /// </summary>
    public static string? Resolve(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',').FirstOrDefault()?.Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }
}
