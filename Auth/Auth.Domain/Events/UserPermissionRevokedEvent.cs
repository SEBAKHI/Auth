using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a direct user permission is revoked.
/// </summary>
public record UserPermissionRevokedEvent(
    Guid UserId,
    Guid PermissionId,
    string PermissionCode,
    Guid RevokedBy) : IDomainEvent;
