namespace Auth.Application.IntegrationEvents.Contracts;

/// <summary>
/// Published when a role is assigned to a user.
/// Consumers: Services that cache user permissions.
/// </summary>
public record RoleAssignedIntegrationEvent(
    Guid UserId,
    Guid RoleId,
    string RoleName,
    Guid AssignedBy,
    DateTime AssignedAt) : IntegrationEvent
{
    public override string EventType => "auth.role.assigned";
}
