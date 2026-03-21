using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an API key is revoked.
/// </summary>
public record ApiKeyRevokedEvent(
    Guid ApiKeyId,
    Guid ApplicationId,
    Guid RevokedBy) : IDomainEvent;
