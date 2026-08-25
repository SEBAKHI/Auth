using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records a permission granted directly to a user. A direct grant bypasses roles entirely, so it is the kind least likely to be noticed by anyone reading the role list.
/// </summary>
public class UserPermissionGrantedAuditEventHandler : INotificationHandler<UserPermissionGrantedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserPermissionGrantedAuditEventHandler> _logger;

    public UserPermissionGrantedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserPermissionGrantedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserPermissionGrantedEvent notification, CancellationToken cancellationToken)
    {
        // No old side: granting a permission the user already holds is refused upstream, so the state before is always "not held".
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Authorization,
            action: AuditActions.PermissionGranted,
            userId: notification.UserId,
            performedBy: notification.GrantedBy,
            applicationId: notification.ApplicationId,
            entityType: "UserPermission",
            entityId: notification.UserId,
            newValues: JsonSerializer.Serialize(new { notification.PermissionId, notification.PermissionCode, notification.ApplicationId, notification.ExpiresAt }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for UserPermissionGrantedEvent");
    }
}
