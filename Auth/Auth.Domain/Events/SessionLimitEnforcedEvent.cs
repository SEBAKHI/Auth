using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// One session the concurrent-session limit ended, named well enough for the
/// account owner to recognise it in an email and for the audit trail to point
/// at the row.
/// </summary>
/// <param name="SessionId">The ended session, so the audit entry can reference it.</param>
/// <param name="DeviceName">Human label such as "Chrome on Windows", or null when unrecognised.</param>
/// <param name="IpAddress">The address that session signed in from.</param>
/// <param name="LastActivityAtUtc">When it was last used — the reason it lost.</param>
public record EndedSession(
    Guid SessionId,
    string? DeviceName,
    string? IpAddress,
    DateTime LastActivityAtUtc);

/// <summary>
/// Raised when a successful sign-in pushed the account over
/// Session:MaxConcurrentSessions and the least recently used sessions were
/// ended to make room.
///
/// Carries every ended session in one event rather than one event per session.
/// An administrator lowering the limit from twenty to five evicts fifteen at
/// the next sign-in, and fifteen emails in one second is how a user learns to
/// ignore security mail. The audit handler still writes a row each.
/// </summary>
/// <param name="EndedSessions">The sessions that lost, never empty.</param>
/// <param name="NewDeviceName">The device that just signed in, so the user can tell cause from effect.</param>
/// <param name="Limit">The configured limit at the moment it was applied.</param>
/// <param name="OccurredAtUtc">When it happened.</param>
public record SessionLimitEnforcedEvent(
    Guid UserId,
    string Email,
    string DisplayName,
    IReadOnlyList<EndedSession> EndedSessions,
    string? NewDeviceName,
    int Limit,
    DateTime OccurredAtUtc) : IDomainEvent;
