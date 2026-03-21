using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a webhook key is revoked.
/// </summary>
public record WebhookKeyRevokedEvent(
    Guid WebhookKeyId,
    Guid ApplicationId,
    Guid RevokedBy) : IDomainEvent;
