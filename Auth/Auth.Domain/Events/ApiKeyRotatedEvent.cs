using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an API key was rotated (OI-128 PR B): a new key now carries the old key's scopes
/// and addresses, and the old one ends at <paramref name="ReplacedKeyExpiresAt"/>. Carries the
/// keys' ids, never their material.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="ApiKeyId">The new key.</param>
/// <param name="ApplicationId">The application both keys belong to.</param>
/// <param name="ReplacedApiKeyId">The rotated key.</param>
/// <param name="ReplacedKeyExpiresAt">When the rotated key stops validating.</param>
/// <param name="TriggeredBy">The administrator who rotated the key.</param>
/// <param name="OccurredAt">When the new key was stored.</param>
public sealed record ApiKeyRotatedEvent(
    Guid EventId,
    Guid ApiKeyId,
    Guid ApplicationId,
    Guid ReplacedApiKeyId,
    DateTime ReplacedKeyExpiresAt,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
