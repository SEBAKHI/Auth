using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records that a platform key was actually rotated or replaced.
/// </summary>
/// <remarks>
/// The only durable record of the most destructive operation the admin API
/// offers. Nothing about the key material is logged — not the value, not the
/// digest the confirmation was bound to — only that this operation ran, who ran
/// it, and which confirmation was spent on it.
/// </remarks>
public class SecretOperationExecutedAuditEventHandler
    : INotificationHandler<SecretOperationExecutedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<SecretOperationExecutedAuditEventHandler> _logger;

    public SecretOperationExecutedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<SecretOperationExecutedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(
        SecretOperationExecutedEvent notification,
        CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Administration",
            action: "secrets.operation.executed",
            userId: notification.ExecutedBy,
            entityType: "SecretOperationChallenge",
            entityId: notification.ChallengeId,
            newValues: JsonSerializer.Serialize(new
            {
                operation = notification.Operation.ToString()
            }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);

        _logger.LogDebug(
            "Audit log created for SecretOperationExecutedEvent ({Operation}) by {ExecutedBy}",
            notification.Operation, notification.ExecutedBy);
    }
}
