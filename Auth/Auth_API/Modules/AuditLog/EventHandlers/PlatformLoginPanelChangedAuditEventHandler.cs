using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when the panel beside the sign-in form was shown or hidden.
/// Recorded under the platform settings' own action, as the logo height is
/// (<see cref="PlatformLogoHeightChangedAuditEventHandler"/>).
/// </summary>
/// <remarks>
/// Best-effort: the save has already committed, so a failure here is logged and never
/// propagated.
/// </remarks>
public class PlatformLoginPanelChangedAuditEventHandler : INotificationHandler<PlatformLoginPanelChangedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<PlatformLoginPanelChangedAuditEventHandler> _logger;

    public PlatformLoginPanelChangedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<PlatformLoginPanelChangedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(PlatformLoginPanelChangedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Administration,
                action: AuditActions.PlatformSettingsUpdated,
                performedBy: notification.TriggeredBy,
                entityType: "PlatformSettings",
                entityId: notification.SettingsId,
                oldValues: JsonSerializer.Serialize(new { loginPanelShown = !notification.Shown }),
                newValues: JsonSerializer.Serialize(new { loginPanelShown = notification.Shown }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for PlatformLoginPanelChangedEvent {EventId}", notification.EventId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for PlatformLoginPanelChangedEvent {EventId}",
                notification.EventId);
        }
    }
}
