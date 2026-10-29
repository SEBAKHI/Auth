using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an application's own appearance changed: switched on or off,
/// or replaced. The row holds the switch and the choices before and after, in the same shape the
/// platform appearance's entry records them.
/// </summary>
/// <remarks>
/// Best-effort, like its siblings for the application's addresses
/// (<see cref="ApplicationLaunchAddressesChangedAuditEventHandler"/>): the save has already
/// committed, so a failure here is logged and never propagated.
/// </remarks>
public class ApplicationAppearanceChangedAuditEventHandler : INotificationHandler<ApplicationAppearanceChangedEvent>
{
    private static readonly JsonSerializerOptions Camel = new(JsonSerializerDefaults.Web);

    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<ApplicationAppearanceChangedAuditEventHandler> _logger;

    public ApplicationAppearanceChangedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<ApplicationAppearanceChangedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(ApplicationAppearanceChangedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Application,
                action: AuditActions.ApplicationAppearanceChanged,
                performedBy: notification.TriggeredBy,
                applicationId: notification.ApplicationId,
                entityType: "Application",
                entityId: notification.ApplicationId,
                oldValues: JsonSerializer.Serialize(notification.Before, Camel),
                newValues: JsonSerializer.Serialize(notification.After, Camel),
                additionalData: JsonSerializer.Serialize(new { applicationCode = notification.ApplicationCode }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for ApplicationAppearanceChangedEvent {EventId}: application {ApplicationId}",
                notification.EventId, notification.ApplicationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for ApplicationAppearanceChangedEvent {EventId}: application {ApplicationId}",
                notification.EventId, notification.ApplicationId);
        }
    }
}
