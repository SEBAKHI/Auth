using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when the languages hidden from the language menus changed.
/// Recorded under the platform settings' own action, as the logo's height is
/// (<see cref="PlatformLogoHeightChangedAuditEventHandler"/>); an empty list hides none.
/// </summary>
/// <remarks>
/// Best-effort: the save has already committed, so a failure here is logged and never
/// propagated.
/// </remarks>
public class PlatformHiddenLanguagesChangedAuditEventHandler : INotificationHandler<PlatformHiddenLanguagesChangedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<PlatformHiddenLanguagesChangedAuditEventHandler> _logger;

    public PlatformHiddenLanguagesChangedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<PlatformHiddenLanguagesChangedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(PlatformHiddenLanguagesChangedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Administration,
                action: AuditActions.PlatformSettingsUpdated,
                performedBy: notification.TriggeredBy,
                entityType: "PlatformSettings",
                entityId: notification.SettingsId,
                oldValues: JsonSerializer.Serialize(new { hiddenLanguages = notification.Before }),
                newValues: JsonSerializer.Serialize(new { hiddenLanguages = notification.After }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for PlatformHiddenLanguagesChangedEvent {EventId}", notification.EventId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for PlatformHiddenLanguagesChangedEvent {EventId}",
                notification.EventId);
        }
    }
}
