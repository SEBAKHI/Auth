using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a system-settings section is saved or
/// reset. The event payloads are override JSON only — secret values can
/// never appear in them (whitelisted writes), so they are logged verbatim.
/// </summary>
public class SystemSettingsUpdatedEventHandler : INotificationHandler<SystemSettingsUpdatedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<SystemSettingsUpdatedEventHandler> _logger;

    public SystemSettingsUpdatedEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<SystemSettingsUpdatedEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(SystemSettingsUpdatedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Administration,
            action: AuditActions.SystemSettingsUpdated,
            performedBy: notification.UpdatedBy,
            entityType: "SystemSettings",
            oldValues: notification.OldOverridesJson,
            newValues: notification.NewOverridesJson,
            additionalData: $"{{\"sectionKey\":\"{notification.SectionKey}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug(
            "Audit log created for SystemSettingsUpdatedEvent ({SectionKey}) by {UpdatedBy}",
            notification.SectionKey, notification.UpdatedBy);
    }
}
