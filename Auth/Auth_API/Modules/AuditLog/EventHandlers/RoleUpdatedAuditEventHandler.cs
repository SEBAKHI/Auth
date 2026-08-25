using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records a role being renamed or re-described, carrying both sides so the row can say what it was before.
/// </summary>
public class RoleUpdatedAuditEventHandler : INotificationHandler<RoleUpdatedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<RoleUpdatedAuditEventHandler> _logger;

    public RoleUpdatedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<RoleUpdatedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(RoleUpdatedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Authorization,
            action: AuditActions.RoleUpdated,
            performedBy: notification.UpdatedBy,
            entityType: "Role",
            entityId: notification.RoleId,
            oldValues: JsonSerializer.Serialize(new { Name = notification.OldName, Description = notification.OldDescription }),
            newValues: JsonSerializer.Serialize(new { Name = notification.NewName, Description = notification.NewDescription }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for RoleUpdatedEvent");
    }
}
