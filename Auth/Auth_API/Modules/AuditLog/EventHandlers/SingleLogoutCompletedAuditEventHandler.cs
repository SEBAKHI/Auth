using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an explicit end of a browser's IdP session ended the sign-ins
/// born from it (single logout, OI-155). Filed as a sign-out (<see cref="AuditActions.UserLogout"/>),
/// with the IdP session, how many sign-ins ended, and what ended it. Critical by nature (sessions
/// ended), written best-effort like every audit handler here until the durable publisher exists:
/// a failure is logged, never propagated, since the sign-out is already done.
/// </summary>
public class SingleLogoutCompletedAuditEventHandler : INotificationHandler<SingleLogoutCompletedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<SingleLogoutCompletedAuditEventHandler> _logger;

    public SingleLogoutCompletedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<SingleLogoutCompletedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(SingleLogoutCompletedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Authentication,
                action: AuditActions.UserLogout,
                userId: notification.UserId,
                performedBy: notification.TriggeredBy,
                entityType: "User",
                entityId: notification.UserId,
                additionalData: JsonSerializer.Serialize(new
                {
                    signedOutBy = "singleLogout",
                    trigger = notification.Trigger,
                    idpSessionId = notification.IdpSessionId,
                    endedSignInCount = notification.EndedSignInCount
                }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for SingleLogoutCompletedEvent {EventId}: {UserId}",
                notification.EventId, notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for SingleLogoutCompletedEvent {EventId}: {UserId}",
                notification.EventId, notification.UserId);
        }
    }
}
