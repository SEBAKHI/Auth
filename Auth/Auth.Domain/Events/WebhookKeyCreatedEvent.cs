using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a new webhook key is created.
/// </summary>
public record WebhookKeyCreatedEvent(
    Guid WebhookKeyId,
    Guid ApplicationId,
    string Name,
    Guid CreatedBy) : IDomainEvent;
