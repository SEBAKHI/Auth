namespace Auth.Domain.ValueObjects;

/// <summary>
/// Where a sign-in came from, as this server observed it: the address, the browser's
/// user agent and the client-supplied device identifier. The session row, the
/// person's sign-in history, the location and the new-device alert are all made from
/// these three, so they travel together.
/// </summary>
/// <remarks>
/// Display and alert facts only. The user agent and the device identifier are
/// client-supplied, so nothing may ever authorize on them. Every member may be null:
/// a request without the header, or a row written before the column.
/// </remarks>
public readonly record struct SignInOrigin
{
    public SignInOrigin(string? ipAddress, string? userAgent, string? deviceId)
    {
        IpAddress = ipAddress;
        UserAgent = userAgent;
        DeviceId = deviceId;
    }

    /// <summary>The client address, as the client address resolver read it.</summary>
    public string? IpAddress { get; }

    /// <summary>The browser's <c>User-Agent</c> header.</summary>
    public string? UserAgent { get; }

    /// <summary>The browser's own identifier (<c>X-Device-Id</c>).</summary>
    public string? DeviceId { get; }
}
