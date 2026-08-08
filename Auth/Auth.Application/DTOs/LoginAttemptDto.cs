using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

/// <summary>
/// One entry in the user's own sign-in history.
///
/// Read-only by nature: this is the record of what happened, not a set of live
/// credentials, so it carries no revoke handle. The email address is absent —
/// the caller is the account owner and already knows it, and echoing it back
/// would put an identifier in a payload that has no use for one.
/// </summary>
public class LoginAttemptDto
{
    public Guid Id { get; set; }

    public DateTime AttemptedAt { get; set; }

    public bool IsSuccess { get; set; }

    /// <summary>
    /// Why it failed, when it did. Free text as stored — the values written do
    /// not match the vocabulary the table's own comment documents, so this is
    /// passed through rather than mapped to an enum that would silently drop
    /// anything unrecognised.
    /// </summary>
    public string? FailureReason { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>Approximate, derived from the IP. Null when it could not be resolved.</summary>
    public string? Location { get; set; }

    /// <summary>Human label parsed from the user agent, e.g. "Chrome on Windows".</summary>
    public string? DeviceName { get; set; }

    public DeviceType DeviceType { get; set; }
}
