using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a role is assigned to a user.
/// </summary>
public record RoleAssignedEvent(
    Guid UserId,
    Guid RoleId,
    string RoleName,
    Guid AssignedBy) : IDomainEvent;
