using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when the platform branding settings are updated.
/// </summary>
public class PlatformSettingsUpdatedAuditEventHandler : INotificationHandler<PlatformSettingsUpdatedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<PlatformSettingsUpdatedAuditEventHandler> _logger;

    public PlatformSettingsUpdatedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<PlatformSettingsUpdatedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(PlatformSettingsUpdatedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Administration",
            action: "platform-settings.updated",
            userId: notification.UpdatedBy,
            entityType: "PlatformSettings",
            entityId: notification.SettingsId,
            oldValues: JsonSerializer.Serialize(new
            {
                platformName = notification.OldPlatformName,
                logoUrl = notification.OldLogoUrl,
                logoUrlDark = notification.OldLogoUrlDark,
                faviconUrl = notification.OldFaviconUrl
            }),
            newValues: JsonSerializer.Serialize(new
            {
                platformName = notification.NewPlatformName,
                logoUrl = notification.NewLogoUrl,
                logoUrlDark = notification.NewLogoUrlDark,
                faviconUrl = notification.NewFaviconUrl
            }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for PlatformSettingsUpdatedEvent by {UpdatedBy}", notification.UpdatedBy);
    }
}
