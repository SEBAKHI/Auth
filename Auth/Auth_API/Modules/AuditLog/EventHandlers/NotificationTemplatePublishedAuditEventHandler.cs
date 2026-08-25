using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a notification template version is published.
/// </summary>
public class NotificationTemplatePublishedAuditEventHandler
    : INotificationHandler<NotificationTemplatePublishedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<NotificationTemplatePublishedAuditEventHandler> _logger;

    public NotificationTemplatePublishedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<NotificationTemplatePublishedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(NotificationTemplatePublishedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Administration,
            action: AuditActions.NotificationTemplatePublished,
            performedBy: notification.PublishedBy,
            entityType: "NotificationTemplate",
            entityId: notification.TemplateId,
            newValues: JsonSerializer.Serialize(new
            {
                notificationTypeId = notification.NotificationTypeId,
                applicationId = notification.ApplicationId,
                channel = notification.Channel.ToString(),
                versionId = notification.VersionId,
                versionNumber = notification.VersionNumber
            }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug(
            "Audit log created for NotificationTemplatePublishedEvent by {PublishedBy}",
            notification.PublishedBy);
    }
}
