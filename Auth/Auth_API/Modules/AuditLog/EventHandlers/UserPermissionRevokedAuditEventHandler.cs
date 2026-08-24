using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records a direct user permission being taken away.
/// </summary>
public class UserPermissionRevokedAuditEventHandler : INotificationHandler<UserPermissionRevokedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserPermissionRevokedAuditEventHandler> _logger;

    public UserPermissionRevokedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserPermissionRevokedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserPermissionRevokedEvent notification, CancellationToken cancellationToken)
    {
        // No new side: the grant is gone. What it was is the part worth keeping.
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Authorization",
            action: "permission.revoked",
            userId: notification.UserId,
            performedBy: notification.RevokedBy,
            entityType: "UserPermission",
            entityId: notification.UserId,
            oldValues: JsonSerializer.Serialize(new { notification.PermissionId, notification.PermissionCode }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for UserPermissionRevokedEvent");
    }
}
