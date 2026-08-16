using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an external provider identity is attached to an account that already existed.
/// </summary>
/// <remarks>
/// Linking happens silently: a provider that asserts a verified address matching a local
/// account grants that account, with no consent step and no notification. That is ordinary
/// SSO behaviour, but it means control of one Google account can become control of a local
/// one, and until this event existed the only trace was a log line. This does not make the
/// link safe - it makes it visible.
/// </remarks>
/// <param name="HoldsWildcardPermission">
/// Whether the account being linked to carries a wildcard permission ("*" or "prefix:*") at
/// the moment of linking. Supplied by the caller because it is a fact about the wider system
/// rather than about the aggregate, in the same way an IP address is on a sign-in event. It
/// is what separates a routine link from one worth waking someone for.
/// </param>
public record ExternalProviderLinkedEvent(
    Guid UserId,
    string Email,
    string Provider,
    string ProviderUserId,
    bool HoldsWildcardPermission) : IDomainEvent;
