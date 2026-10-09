using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when the platform appearance is replaced. It is
/// recorded under the same action as a branding change: both are edits of the
/// platform settings, told apart by the values recorded.
/// </summary>
public class PlatformThemeUpdatedAuditEventHandler : INotificationHandler<PlatformThemeUpdatedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<PlatformThemeUpdatedAuditEventHandler> _logger;

    public PlatformThemeUpdatedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<PlatformThemeUpdatedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(PlatformThemeUpdatedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Administration,
            action: AuditActions.PlatformSettingsUpdated,
            performedBy: notification.UpdatedBy,
            entityType: "PlatformSettings",
            entityId: notification.SettingsId,
            oldValues: JsonSerializer.Serialize(new { theme = Describe(notification.OldTheme) }),
            newValues: JsonSerializer.Serialize(new { theme = Describe(notification.NewTheme) }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for PlatformThemeUpdatedEvent by {UpdatedBy}", notification.UpdatedBy);
    }

    private static object Describe(PlatformTheme theme) => new
    {
        baseColor = Describe(theme.Base),
        theme = Describe(theme.Theme),
        chart = Describe(theme.Chart),
        radius = theme.Radius,
        menuAccent = theme.MenuAccent,
    };

    private static object Describe(ThemeColorChoice choice) =>
        new { preset = choice.Preset, light = choice.Light, dark = choice.Dark };
}
