using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a role is assigned to a user.
/// </summary>
/// <param name="ApplicationId">
/// The scope the assignment was written at: the application whose tokens carry the
/// role, or null for the platform.
/// </param>
public record RoleAssignedEvent(
    Guid UserId,
    Guid RoleId,
    string RoleName,
    Guid AssignedBy,
    Guid? ApplicationId = null) : IDomainEvent;
