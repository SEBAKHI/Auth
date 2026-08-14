using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a user is invited to a restricted application.
/// </summary>
public record ApplicationAccessGrantedEvent(
    Guid ApplicationId,
    string ApplicationCode,
    Guid UserId,
    DateTime? ExpiresAt,
    Guid GrantedBy) : IDomainEvent;
