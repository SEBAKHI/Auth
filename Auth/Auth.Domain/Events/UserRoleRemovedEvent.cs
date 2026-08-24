using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a role assignment is removed from a user.
/// </summary>
public record UserRoleRemovedEvent(
    Guid UserId,
    Guid RoleId,
    string RoleName,
    Guid? ApplicationId,
    Guid RemovedBy) : IDomainEvent;
