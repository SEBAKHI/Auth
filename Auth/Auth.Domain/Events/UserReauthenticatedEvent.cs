using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a signed-in person proved themself again inside a session, with no new sign-in, so
/// that session may change a security setting for the next few minutes.
/// </summary>
/// <remarks>
/// For the audit row only: no notice goes to the owner, because nothing about the account changed.
/// A change the proof then allows sends its own.
/// </remarks>
public record UserReauthenticatedEvent(
    Guid UserId,
    Guid SessionId,
    AuthenticationMethods Methods) : IDomainEvent;
