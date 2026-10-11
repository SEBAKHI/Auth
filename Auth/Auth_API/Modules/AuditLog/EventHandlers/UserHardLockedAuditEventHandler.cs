using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an authenticator's consecutive failures reach
/// the hard limit: <c>UserId</c> is the locked account; there is no actor, because
/// wrong guesses identify nobody.
/// </summary>
/// <remarks>
/// Critical: this row is how an administrator learns that an account is locked
/// until its owner resets the password. A failure to write it is caught, never
/// propagated — the lock has already been written, and an exception here would
/// turn the sign-in's answer into an error — and logged once at Error with
/// <see cref="AuditWriteFailedEventCode"/>, the only record of the missing row.
/// </remarks>
public class UserHardLockedAuditEventHandler : INotificationHandler<UserHardLockedEvent>
{
    /// <summary>The event code of the line written when the audit row could not be.</summary>
    public const string AuditWriteFailedEventCode = "audit.write-failed";

    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserHardLockedAuditEventHandler> _logger;

    public UserHardLockedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserHardLockedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserHardLockedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Security,
                action: AuditActions.UserHardLocked,
                userId: notification.UserId,
                performedBy: notification.TriggeredBy,
                entityType: "User",
                entityId: notification.UserId,
                additionalData: JsonSerializer.Serialize(new
                {
                    eventId = notification.EventId,
                    factor = notification.Factor.ToString(),
                    failedAttempts = notification.FailedAttempts,
                    occurredAt = notification.OccurredAt
                }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug("Audit log created for UserHardLockedEvent: {UserId}", notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "{EventCode}: the audit row for UserHardLockedEvent {EventId} on account {UserId} was not written",
                AuditWriteFailedEventCode, notification.EventId, notification.UserId);
        }
    }
}
