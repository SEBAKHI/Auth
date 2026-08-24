using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a notification template is rolled back to a
/// previous version.
/// </summary>
public class NotificationTemplateRolledBackAuditEventHandler
    : INotificationHandler<NotificationTemplateRolledBackEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<NotificationTemplateRolledBackAuditEventHandler> _logger;

    public NotificationTemplateRolledBackAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<NotificationTemplateRolledBackAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(NotificationTemplateRolledBackEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Administration",
            action: "notification-template.rolled-back",
            performedBy: notification.RolledBackBy,
            entityType: "NotificationTemplate",
            entityId: notification.TemplateId,
            oldValues: JsonSerializer.Serialize(new { fromVersionId = notification.FromVersionId }),
            newValues: JsonSerializer.Serialize(new
            {
                notificationTypeId = notification.NotificationTypeId,
                applicationId = notification.ApplicationId,
                channel = notification.Channel.ToString(),
                toVersionId = notification.ToVersionId,
                toVersionNumber = notification.ToVersionNumber
            }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug(
            "Audit log created for NotificationTemplateRolledBackEvent by {RolledBackBy}",
            notification.RolledBackBy);
    }
}
