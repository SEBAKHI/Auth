using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a signed-in person gave the right password, or the right emailed code of an account
/// without one, while proving themself again, and the second step was then refused
/// (REVIEW-FIXES 152 F25): whoever holds the first secret is trying the second.
/// </summary>
/// <remarks>
/// For the audit row only, under the proof's own action with no success: no new action, so the
/// console's catalog does not move. Nothing here is counted against the address: the per-address
/// ceiling counts wrong passwords, which this is not.
/// </remarks>
/// <param name="UserId">The account.</param>
/// <param name="SessionId">The session the proof was for.</param>
/// <param name="Reason">The published code the second step was refused with; never the code given.</param>
/// <param name="IpAddress">The address the proof came from.</param>
/// <param name="UserAgent">The browser it came from.</param>
public record UserReauthenticationFailedEvent(
    Guid UserId,
    Guid SessionId,
    string Reason,
    string? IpAddress,
    string? UserAgent) : IDomainEvent;
