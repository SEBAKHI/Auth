using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an application role is assigned to a member within an organization
/// (OrganizationUserRoles): the role then rides in the member's tokens for that
/// application. A re-assignment of an expired or inactive row raises it too.
/// </summary>
public record OrganizationAppRoleAssignedEvent(
    Guid OrganizationId,
    Guid UserId,
    Guid ApplicationId,
    Guid RoleId,
    string RoleName,
    DateTime? ExpiresAt,
    Guid AssignedBy) : IDomainEvent;
