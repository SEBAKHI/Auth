using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an explicit end of a browser's IdP session ended the sign-ins born from it (single
/// logout, OI-155): each application's and platform app's session whose refresh chain was linked
/// to that IdP session. Raised only when at least one sign-in ended.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="UserId">The person whose sign-ins ended.</param>
/// <param name="IdpSessionId">The IdP session that was ended in the browser.</param>
/// <param name="EndedSignInCount">How many sign-ins ended: sessions, and tokens issued without one.</param>
/// <param name="Trigger">What ended the IdP session, one of <see cref="Constants.SingleLogoutTriggers"/>.</param>
/// <param name="TriggeredBy">
/// The account that caused it: the person signing out, or, when another person signed in on the
/// browser, that other person.
/// </param>
/// <param name="OccurredAt">When the sign-ins ended.</param>
public sealed record SingleLogoutCompletedEvent(
    Guid EventId,
    Guid UserId,
    Guid IdpSessionId,
    int EndedSignInCount,
    string Trigger,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
