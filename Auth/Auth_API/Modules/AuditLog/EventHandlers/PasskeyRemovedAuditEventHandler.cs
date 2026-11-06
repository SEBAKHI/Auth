using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry for every passkey that stopped working (S20): removed by its
/// owner, revoked by an administrator's reset, or revoked by a password reset, a password
/// change or an administrator's lock because it was registered in the 72 hours before.
/// </summary>
/// <remarks>
/// Best-effort: a failure to write the row is logged, never propagated, for the reason the
/// registration's handler gives. The details (the name and the reason) are built with
/// System.Text.Json (ARR-1058). No credential id, public key or user handle is recorded
/// (PK16).
/// </remarks>
public sealed class PasskeyRemovedAuditEventHandler : INotificationHandler<PasskeyRemovedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<PasskeyRemovedAuditEventHandler> _logger;

    public PasskeyRemovedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<PasskeyRemovedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(PasskeyRemovedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Security,
                action: AuditActions.PasskeyRemoved,
                userId: notification.UserId,
                performedBy: notification.RemovedBy,
                entityType: "UserPasskey",
                entityId: notification.PasskeyId,
                additionalData: JsonSerializer.Serialize(new { name = notification.Name, reason = notification.Reason }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for PasskeyRemovedEvent: passkey {PasskeyId} of user {UserId} ({Reason})",
                notification.PasskeyId, notification.UserId, notification.Reason);
        }
    }
}
