using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when one of the platform's fonts was uploaded, replaced or
/// removed. Recorded under the platform settings' own action, as the logo height is
/// (<see cref="PlatformLogoHeightChangedAuditEventHandler"/>); the values name the role, the
/// font's family and its stored file, null where there is none (the shipped font).
/// </summary>
/// <remarks>
/// Best-effort: the save has already committed, so a failure here is logged and never
/// propagated.
/// </remarks>
public class PlatformFontChangedAuditEventHandler : INotificationHandler<PlatformFontChangedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<PlatformFontChangedAuditEventHandler> _logger;

    public PlatformFontChangedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<PlatformFontChangedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(PlatformFontChangedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var role = notification.Role.ToString().ToLowerInvariant();
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Administration,
                action: AuditActions.PlatformSettingsUpdated,
                performedBy: notification.TriggeredBy,
                entityType: "PlatformSettings",
                entityId: notification.SettingsId,
                oldValues: JsonSerializer.Serialize(new
                {
                    font = role,
                    familyName = notification.BeforeFamilyName,
                    file = notification.BeforeKey
                }),
                newValues: JsonSerializer.Serialize(new
                {
                    font = role,
                    familyName = notification.AfterFamilyName,
                    file = notification.AfterKey
                }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for PlatformFontChangedEvent {EventId}", notification.EventId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for PlatformFontChangedEvent {EventId}",
                notification.EventId);
        }
    }
}
