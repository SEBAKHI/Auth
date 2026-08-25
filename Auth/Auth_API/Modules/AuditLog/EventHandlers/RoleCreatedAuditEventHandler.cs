using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records a role being created.
/// </summary>
public class RoleCreatedAuditEventHandler : INotificationHandler<RoleCreatedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<RoleCreatedAuditEventHandler> _logger;

    public RoleCreatedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<RoleCreatedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(RoleCreatedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Authorization,
            action: AuditActions.RoleCreated,
            performedBy: notification.CreatedBy,
            applicationId: notification.ApplicationId,
            entityType: "Role",
            entityId: notification.RoleId,
            newValues: JsonSerializer.Serialize(new { notification.RoleCode, notification.RoleName, notification.ApplicationId }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for RoleCreatedEvent");
    }
}
