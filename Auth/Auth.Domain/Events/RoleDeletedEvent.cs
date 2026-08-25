using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a role is deleted, taking its permissions from everyone who held it.
/// </summary>
public record RoleDeletedEvent(
    Guid RoleId,
    string RoleCode,
    string RoleName,
    Guid DeletedBy) : IDomainEvent;
