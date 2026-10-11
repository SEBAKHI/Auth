using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an application's machine key was revoked (OI-128). Critical by
/// nature (a credential was withdrawn), written best-effort like every audit handler here until the
/// durable publisher exists: a failure is logged, never propagated.
/// </summary>
public class MachineKeyRevokedAuditEventHandler : INotificationHandler<MachineKeyRevokedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<MachineKeyRevokedAuditEventHandler> _logger;

    public MachineKeyRevokedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<MachineKeyRevokedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(MachineKeyRevokedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.ApiKeyManagement,
                action: AuditActions.MachineKeyRevoked,
                performedBy: notification.TriggeredBy,
                applicationId: notification.ApplicationId,
                entityType: "ApplicationMachineKey",
                entityId: notification.MachineKeyId,
                additionalData: JsonSerializer.Serialize(new { kid = notification.Kid }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for MachineKeyRevokedEvent {EventId}: {MachineKeyId}",
                notification.EventId, notification.MachineKeyId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for MachineKeyRevokedEvent {EventId}: {MachineKeyId}",
                notification.EventId, notification.MachineKeyId);
        }
    }
}
