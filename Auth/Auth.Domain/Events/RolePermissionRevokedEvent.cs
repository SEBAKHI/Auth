using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a permission is removed from a role, which takes it from every holder of that role at once.
/// </summary>
public record RolePermissionRevokedEvent(
    Guid RoleId,
    string RoleName,
    Guid PermissionId,
    string PermissionCode,
    Guid RevokedBy) : IDomainEvent;
