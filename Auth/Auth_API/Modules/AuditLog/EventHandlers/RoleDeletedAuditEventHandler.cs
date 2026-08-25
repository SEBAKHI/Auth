using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records a role being deleted, which takes its permissions from everyone who held it.
/// </summary>
public class RoleDeletedAuditEventHandler : INotificationHandler<RoleDeletedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<RoleDeletedAuditEventHandler> _logger;

    public RoleDeletedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<RoleDeletedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(RoleDeletedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Authorization,
            action: AuditActions.RoleDeleted,
            performedBy: notification.DeletedBy,
            entityType: "Role",
            entityId: notification.RoleId,
            oldValues: JsonSerializer.Serialize(new { notification.RoleCode, notification.RoleName }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for RoleDeletedEvent");
    }
}
