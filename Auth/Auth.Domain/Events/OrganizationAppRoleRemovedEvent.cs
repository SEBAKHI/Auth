using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an application role a member held within an organization is removed:
/// raised only when a row was removed.
/// </summary>
public record OrganizationAppRoleRemovedEvent(
    Guid OrganizationId,
    Guid UserId,
    Guid ApplicationId,
    Guid RoleId,
    string RoleName,
    Guid RemovedBy) : IDomainEvent;
