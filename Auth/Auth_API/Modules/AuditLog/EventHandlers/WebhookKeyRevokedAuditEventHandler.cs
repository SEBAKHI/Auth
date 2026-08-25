using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records a webhook key being revoked. Its sibling event had no handler either, so neither end of a signing credential lifetime was written down.
/// </summary>
public class WebhookKeyRevokedAuditEventHandler : INotificationHandler<WebhookKeyRevokedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<WebhookKeyRevokedAuditEventHandler> _logger;

    public WebhookKeyRevokedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<WebhookKeyRevokedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(WebhookKeyRevokedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Security,
            action: AuditActions.WebhookKeyRevoked,
            performedBy: notification.RevokedBy,
            applicationId: notification.ApplicationId,
            entityType: "WebhookKey",
            entityId: notification.WebhookKeyId,
            oldValues: JsonSerializer.Serialize(new { notification.WebhookKeyId }));
        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for WebhookKeyRevokedEvent");
    }
}
