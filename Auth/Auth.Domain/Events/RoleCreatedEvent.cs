using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a role is created.
/// </summary>
public record RoleCreatedEvent(
    Guid RoleId,
    string RoleCode,
    string RoleName,
    Guid? ApplicationId,
    Guid CreatedBy) : IDomainEvent;
