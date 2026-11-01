using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when the height of the platform's logo on the sign-in pages
/// changed. Recorded under the platform settings' own action, as the appearance is
/// (<see cref="PlatformThemeUpdatedAuditEventHandler"/>); a null height is the default.
/// </summary>
/// <remarks>
/// Best-effort: the save has already committed, so a failure here is logged and never
/// propagated.
/// </remarks>
public class PlatformLogoHeightChangedAuditEventHandler : INotificationHandler<PlatformLogoHeightChangedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<PlatformLogoHeightChangedAuditEventHandler> _logger;

    public PlatformLogoHeightChangedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<PlatformLogoHeightChangedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(PlatformLogoHeightChangedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Administration,
                action: AuditActions.PlatformSettingsUpdated,
                performedBy: notification.TriggeredBy,
                entityType: "PlatformSettings",
                entityId: notification.SettingsId,
                oldValues: JsonSerializer.Serialize(new { logoHeightPx = notification.BeforePx }),
                newValues: JsonSerializer.Serialize(new { logoHeightPx = notification.AfterPx }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for PlatformLogoHeightChangedEvent {EventId}", notification.EventId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for PlatformLogoHeightChangedEvent {EventId}",
                notification.EventId);
        }
    }
}
