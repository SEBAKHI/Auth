using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a new API key is created.
/// </summary>
public record ApiKeyCreatedEvent(
    Guid ApiKeyId,
    Guid ApplicationId,
    string Name,
    Guid CreatedBy) : IDomainEvent;
