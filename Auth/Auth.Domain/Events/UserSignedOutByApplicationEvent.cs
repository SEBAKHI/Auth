using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a server-side application signed a person out with its sign-in key and the
/// session's bound refresh token (OI-183): the application's session at this server ended, and
/// with it, when the token was linked to one, the IdP session of the browser the person signed
/// in from.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="UserId">The person signed out.</param>
/// <param name="ApplicationId">The application whose key authenticated the request.</param>
/// <param name="SessionId">The application session that ended; null for a token issued without one.</param>
/// <param name="SingleSignOnEnded">
/// Whether the IdP session the token was linked to no longer signs its browser in. False when the
/// token carried no link (issued before the link existed), so that browser may still be signed in.
/// </param>
/// <param name="TriggeredBy">The person: the application acted for them, with their session's token.</param>
/// <param name="OccurredAt">When the sessions ended.</param>
public sealed record UserSignedOutByApplicationEvent(
    Guid EventId,
    Guid UserId,
    Guid ApplicationId,
    Guid? SessionId,
    bool SingleSignOnEnded,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
