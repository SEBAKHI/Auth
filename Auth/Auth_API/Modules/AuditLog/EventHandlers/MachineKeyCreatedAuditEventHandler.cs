using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an application received a machine key (OI-128). Critical by
/// nature (a credential was issued), written best-effort like every audit handler here until the
/// durable publisher exists: a failure is logged, never propagated.
/// </summary>
/// <remarks>
/// The row names the key and how it came to be, never its material. The details are serialized,
/// not interpolated: no free text of the request reaches them, and none ever could break them.
/// </remarks>
public class MachineKeyCreatedAuditEventHandler : INotificationHandler<MachineKeyCreatedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<MachineKeyCreatedAuditEventHandler> _logger;

    public MachineKeyCreatedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<MachineKeyCreatedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(MachineKeyCreatedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.ApiKeyManagement,
                action: AuditActions.MachineKeyCreated,
                performedBy: notification.TriggeredBy,
                applicationId: notification.ApplicationId,
                entityType: "ApplicationMachineKey",
                entityId: notification.MachineKeyId,
                additionalData: JsonSerializer.Serialize(new
                {
                    kid = notification.Kid,
                    generated = notification.Generated,
                    purpose = notification.Purpose.ToString()
                }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for MachineKeyCreatedEvent {EventId}: {MachineKeyId}",
                notification.EventId, notification.MachineKeyId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for MachineKeyCreatedEvent {EventId}: {MachineKeyId}",
                notification.EventId, notification.MachineKeyId);
        }
    }
}
