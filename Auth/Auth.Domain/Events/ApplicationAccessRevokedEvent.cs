using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a user's invitation to an application is withdrawn.
/// </summary>
public record ApplicationAccessRevokedEvent(
    Guid ApplicationId,
    string ApplicationCode,
    Guid UserId,
    Guid RevokedBy) : IDomainEvent;
