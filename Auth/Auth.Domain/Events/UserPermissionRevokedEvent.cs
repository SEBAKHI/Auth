using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a direct user permission is revoked. <see cref="ApplicationId"/> is
/// the scope of the grant that went, null for a platform-wide one.
/// </summary>
public record UserPermissionRevokedEvent(
    Guid UserId,
    Guid PermissionId,
    string PermissionCode,
    Guid? ApplicationId,
    Guid RevokedBy) : IDomainEvent;
