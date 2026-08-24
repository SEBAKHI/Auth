using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records a role assignment being removed from a user, which takes every permission that role carried with it.
/// </summary>
public class UserRoleRemovedAuditEventHandler : INotificationHandler<UserRoleRemovedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserRoleRemovedAuditEventHandler> _logger;

    public UserRoleRemovedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserRoleRemovedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserRoleRemovedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Authorization",
            action: "role.removed",
            userId: notification.UserId,
            performedBy: notification.RemovedBy,
            applicationId: notification.ApplicationId,
            entityType: "UserRole",
            entityId: notification.UserId,
            oldValues: JsonSerializer.Serialize(new { notification.RoleId, notification.RoleName, notification.ApplicationId }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for UserRoleRemovedEvent");
    }
}
