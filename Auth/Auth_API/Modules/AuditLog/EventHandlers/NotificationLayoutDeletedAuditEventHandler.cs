using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an application's notification layout was deleted (the global
/// layout of a channel cannot be). The row keeps the layout's name and channel, the only trace left of it.
/// </summary>
/// <remarks>
/// Best-effort, like its sibling for deleted sign-in page designs: the change has already
/// committed, so a failure here is logged and never propagated. The row never carries the
/// layout's content.
/// </remarks>
public class NotificationLayoutDeletedAuditEventHandler : INotificationHandler<NotificationLayoutDeletedEvent>
{
    private static readonly JsonSerializerOptions Camel = new(JsonSerializerDefaults.Web);

    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<NotificationLayoutDeletedAuditEventHandler> _logger;

    public NotificationLayoutDeletedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<NotificationLayoutDeletedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(NotificationLayoutDeletedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Administration,
                action: AuditActions.NotificationLayoutDeleted,
                performedBy: notification.TriggeredBy,
                applicationId: notification.ApplicationId,
                entityType: "NotificationLayout",
                entityId: notification.NotificationLayoutId,
                oldValues: JsonSerializer.Serialize(
                    new { notification.Name, Channel = notification.Channel.ToString() }, Camel),
                newValues: null,
                additionalData: null);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for NotificationLayoutDeletedEvent {EventId}: layout {LayoutId}",
                notification.EventId, notification.NotificationLayoutId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for NotificationLayoutDeletedEvent {EventId}: layout {LayoutId}",
                notification.EventId, notification.NotificationLayoutId);
        }
    }
}
