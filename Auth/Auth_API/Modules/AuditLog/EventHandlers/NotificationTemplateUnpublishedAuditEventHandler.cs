using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a notification template is unpublished.
/// </summary>
public class NotificationTemplateUnpublishedAuditEventHandler
    : INotificationHandler<NotificationTemplateUnpublishedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<NotificationTemplateUnpublishedAuditEventHandler> _logger;

    public NotificationTemplateUnpublishedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<NotificationTemplateUnpublishedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(NotificationTemplateUnpublishedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Administration",
            action: "notification-template.unpublished",
            userId: notification.UnpublishedBy,
            entityType: "NotificationTemplate",
            entityId: notification.TemplateId,
            oldValues: JsonSerializer.Serialize(new
            {
                notificationTypeId = notification.NotificationTypeId,
                applicationId = notification.ApplicationId,
                channel = notification.Channel.ToString(),
                unpublishedVersionId = notification.UnpublishedVersionId
            }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug(
            "Audit log created for NotificationTemplateUnpublishedEvent by {UnpublishedBy}",
            notification.UnpublishedBy);
    }
}
