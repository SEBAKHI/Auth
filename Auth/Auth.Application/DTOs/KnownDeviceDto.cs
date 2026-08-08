using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

/// <summary>
/// A browser the user has signed in from before.
///
/// The signature itself is deliberately absent: it is derived from a value the
/// client holds, and publishing it would let anything that can read one response
/// recognise the same browser elsewhere. The row id is enough to act on.
/// </summary>
public class KnownDeviceDto
{
    public Guid Id { get; set; }

    /// <summary>Human label, e.g. "Chrome on Windows". Null when the agent named nothing recognisable.</summary>
    public string? DeviceName { get; set; }

    /// <summary>Form factor, carried over from the most recent session on this browser.</summary>
    public DeviceType DeviceType { get; set; }

    public DateTime FirstSeenAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    /// <summary>How many of the user's live sessions were started from this browser.</summary>
    public int ActiveSessionCount { get; set; }

    /// <summary>
    /// Whether the caller's own session is one of them. Drives both the badge and
    /// the refusal to forget it.
    /// </summary>
    public bool IsCurrent { get; set; }
}
