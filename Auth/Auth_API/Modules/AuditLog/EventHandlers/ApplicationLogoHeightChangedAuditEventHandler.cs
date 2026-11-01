using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when the height of an application's logo on its sign-in pages
/// changed, with the height before and after (null is the default).
/// </summary>
/// <remarks>
/// Best-effort, like <see cref="ApplicationAppearanceChangedAuditEventHandler"/>: the save has
/// already committed, so a failure here is logged and never propagated.
/// </remarks>
public class ApplicationLogoHeightChangedAuditEventHandler : INotificationHandler<ApplicationLogoHeightChangedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<ApplicationLogoHeightChangedAuditEventHandler> _logger;

    public ApplicationLogoHeightChangedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<ApplicationLogoHeightChangedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(ApplicationLogoHeightChangedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Application,
                action: AuditActions.ApplicationLogoHeightChanged,
                performedBy: notification.TriggeredBy,
                applicationId: notification.ApplicationId,
                entityType: "Application",
                entityId: notification.ApplicationId,
                oldValues: JsonSerializer.Serialize(new { logoHeightPx = notification.BeforePx }),
                newValues: JsonSerializer.Serialize(new { logoHeightPx = notification.AfterPx }),
                additionalData: JsonSerializer.Serialize(new { applicationCode = notification.ApplicationCode }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for ApplicationLogoHeightChangedEvent {EventId}: application {ApplicationId}",
                notification.EventId, notification.ApplicationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for ApplicationLogoHeightChangedEvent {EventId}: application {ApplicationId}",
                notification.EventId, notification.ApplicationId);
        }
    }
}
