using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records a permission added to a role. It reaches every current holder of that role at once, which makes it a wider change than a direct grant and an easier one to make by accident.
/// </summary>
public class RolePermissionGrantedAuditEventHandler : INotificationHandler<RolePermissionGrantedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<RolePermissionGrantedAuditEventHandler> _logger;

    public RolePermissionGrantedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<RolePermissionGrantedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(RolePermissionGrantedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Authorization",
            action: "role.permission.granted",
            performedBy: notification.GrantedBy,
            entityType: "RolePermission",
            entityId: notification.RoleId,
            newValues: JsonSerializer.Serialize(new { notification.RoleName, notification.PermissionId, notification.PermissionCode }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for RolePermissionGrantedEvent");
    }
}
