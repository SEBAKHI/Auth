using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records a permission removed from a role, taking it from every holder at once.
/// </summary>
public class RolePermissionRevokedAuditEventHandler : INotificationHandler<RolePermissionRevokedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<RolePermissionRevokedAuditEventHandler> _logger;

    public RolePermissionRevokedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<RolePermissionRevokedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(RolePermissionRevokedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Authorization,
            action: AuditActions.RolePermissionRevoked,
            performedBy: notification.RevokedBy,
            entityType: "RolePermission",
            entityId: notification.RoleId,
            oldValues: JsonSerializer.Serialize(new { notification.RoleName, notification.PermissionId, notification.PermissionCode }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for RolePermissionRevokedEvent");
    }
}
