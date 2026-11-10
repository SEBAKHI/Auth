using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a signed-in person proved themself again inside a session, with no new sign-in, so
/// that session may change a security setting for the next few minutes.
/// </summary>
/// <remarks>
/// For the audit row only: no notice goes to the owner, because nothing about the account changed.
/// A change the proof then allows sends its own. The row carries where the proof came from, as a
/// sign-in's row does (REVIEW-FIXES 152 F25): the "sign in again" this replaced left one.
/// </remarks>
/// <param name="UserId">The account.</param>
/// <param name="SessionId">The session the proof stamped.</param>
/// <param name="Methods">What this one proof proved.</param>
/// <param name="IpAddress">The address the proof came from.</param>
/// <param name="UserAgent">The browser it came from.</param>
public record UserReauthenticatedEvent(
    Guid UserId,
    Guid SessionId,
    AuthenticationMethods Methods,
    string? IpAddress,
    string? UserAgent) : IDomainEvent;
