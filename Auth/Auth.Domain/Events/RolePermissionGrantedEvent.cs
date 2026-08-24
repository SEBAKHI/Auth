using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a permission is added to a role, which grants it to every holder of that role at once.
/// </summary>
public record RolePermissionGrantedEvent(
    Guid RoleId,
    string RoleName,
    Guid PermissionId,
    string PermissionCode,
    Guid GrantedBy) : IDomainEvent;
