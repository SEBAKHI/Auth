using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records that someone asked to rotate or replace a platform key.
/// </summary>
/// <remarks>
/// Logged even though nothing was changed. The request is the interesting event
/// when it was not the named administrator who made it: an attempt that never
/// completed — because the code went to a mailbox the attacker does not hold —
/// is exactly the signal an incident responder needs, and it leaves no other
/// trace.
/// </remarks>
public class SecretOperationChallengeIssuedAuditEventHandler
    : INotificationHandler<SecretOperationChallengeIssuedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<SecretOperationChallengeIssuedAuditEventHandler> _logger;

    public SecretOperationChallengeIssuedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<SecretOperationChallengeIssuedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(
        SecretOperationChallengeIssuedEvent notification,
        CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Administration",
            action: "secrets.operation.confirmation-requested",
            userId: notification.RequestedBy,
            entityType: "SecretOperationChallenge",
            entityId: notification.ChallengeId,
            ipAddress: notification.IpAddress,
            newValues: JsonSerializer.Serialize(new
            {
                operation = notification.Operation.ToString()
            }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);

        _logger.LogDebug(
            "Audit log created for SecretOperationChallengeIssuedEvent ({Operation}) by {RequestedBy}",
            notification.Operation, notification.RequestedBy);
    }
}
