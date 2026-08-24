using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records a webhook key being created. The event was raised from the day the feature shipped and had no handler, so a credential that signs outbound calls came into existence with nothing recording it.
/// </summary>
public class WebhookKeyCreatedAuditEventHandler : INotificationHandler<WebhookKeyCreatedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<WebhookKeyCreatedAuditEventHandler> _logger;

    public WebhookKeyCreatedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<WebhookKeyCreatedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(WebhookKeyCreatedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Security",
            action: "webhookkey.created",
            performedBy: notification.CreatedBy,
            applicationId: notification.ApplicationId,
            entityType: "WebhookKey",
            entityId: notification.WebhookKeyId,
            newValues: JsonSerializer.Serialize(new { notification.Name }));
        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for WebhookKeyCreatedEvent");
    }
}
