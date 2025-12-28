using MediatR;

namespace Auth_Lib.Domain.Events;

/// <summary>
/// Published when a role is assigned to a user.
/// </summary>
public record RoleAssignedEvent(
    Guid UserId,
    Guid RoleId,
    string RoleName,
    Guid AssignedBy) : INotification;
