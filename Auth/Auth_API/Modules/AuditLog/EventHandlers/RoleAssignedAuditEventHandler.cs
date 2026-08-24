using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a role is assigned to a user.
/// </summary>
public class RoleAssignedAuditEventHandler : INotificationHandler<RoleAssignedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<RoleAssignedAuditEventHandler> _logger;

    public RoleAssignedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<RoleAssignedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(RoleAssignedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Authorization",
            action: "role.assigned",
            userId: notification.UserId,
            performedBy: notification.AssignedBy,
            entityType: "UserRole",
            entityId: notification.UserId,
            additionalData: $"{{\"roleId\":\"{notification.RoleId}\",\"roleName\":\"{notification.RoleName}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for RoleAssignedEvent: User {UserId}, Role {RoleId}",
            notification.UserId, notification.RoleId);
    }
}
