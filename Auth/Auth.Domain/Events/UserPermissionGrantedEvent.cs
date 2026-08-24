using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a permission is granted directly to a user, outside any role.
/// </summary>
public record UserPermissionGrantedEvent(
    Guid UserId,
    Guid PermissionId,
    string PermissionCode,
    Guid? ApplicationId,
    DateTime? ExpiresAt,
    Guid GrantedBy) : IDomainEvent;
