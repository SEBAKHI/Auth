using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an application's machine directory consent was switched on or
/// off (OI-202). Written best-effort like every audit handler here until the durable publisher
/// exists: a failure is logged, never propagated.
/// </summary>
public class MachineOrganizationsReadChangedAuditEventHandler : INotificationHandler<MachineOrganizationsReadChangedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<MachineOrganizationsReadChangedAuditEventHandler> _logger;

    public MachineOrganizationsReadChangedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<MachineOrganizationsReadChangedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(MachineOrganizationsReadChangedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.ApiKeyManagement,
                action: notification.Allowed
                    ? AuditActions.MachineOrganizationsReadAllowed
                    : AuditActions.MachineOrganizationsReadWithdrawn,
                performedBy: notification.TriggeredBy,
                applicationId: notification.ApplicationId,
                entityType: "Application",
                entityId: notification.ApplicationId);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for MachineOrganizationsReadChangedEvent {EventId}: {ApplicationId}",
                notification.EventId, notification.ApplicationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for MachineOrganizationsReadChangedEvent {EventId}: {ApplicationId}",
                notification.EventId, notification.ApplicationId);
        }
    }
}
