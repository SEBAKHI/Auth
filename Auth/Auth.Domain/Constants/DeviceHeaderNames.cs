namespace Auth.Domain.Constants;

/// <summary>
/// Transport names for the client facts that identify a browser.
///
/// A constant rather than a literal at each call site: the header is set by the
/// SPA's API client and read by the controllers, and the two only agree because
/// they spell it the same way. A typo here fails the way the defect this
/// replaces failed — silently, as a browser nobody recognises.
/// </summary>
public static class DeviceHeaderNames
{
    /// <summary>
    /// Carries the client's per-browser identifier. Mirrored by
    /// <c>DEVICE_ID_HEADER</c> in <c>Auth_UI/packages/api/src/device-id.ts</c>.
    /// </summary>
    public const string DeviceId = "X-Device-Id";

    /// <summary>
    /// Matches <c>UserSessions.DeviceId</c>'s column width. Anything longer is
    /// truncated at the edge rather than allowed to reach a signature or an
    /// INSERT — a client can send whatever it likes here.
    /// </summary>
    public const int MaxDeviceIdLength = 64;
}
