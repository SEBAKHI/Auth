using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a server-side application signed a person out from its server
/// (OI-183). Filed as a sign-out of one device (<see cref="AuditActions.UserLogout"/>), with the
/// application, the session, and whether the browser's single sign-on session ended. Critical by
/// nature (a session ended), written best-effort like every audit handler here until the durable
/// publisher exists: a failure is logged, never propagated, since the sign-out is already done.
/// </summary>
public class UserSignedOutByApplicationAuditEventHandler : INotificationHandler<UserSignedOutByApplicationEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserSignedOutByApplicationAuditEventHandler> _logger;

    public UserSignedOutByApplicationAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserSignedOutByApplicationAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserSignedOutByApplicationEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Authentication,
                action: AuditActions.UserLogout,
                userId: notification.UserId,
                performedBy: notification.TriggeredBy,
                applicationId: notification.ApplicationId,
                entityType: "User",
                entityId: notification.UserId,
                additionalData: JsonSerializer.Serialize(new
                {
                    signedOutBy = "application",
                    singleSignOnEnded = notification.SingleSignOnEnded
                }),
                sessionId: notification.SessionId);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for UserSignedOutByApplicationEvent {EventId}: {UserId}",
                notification.EventId, notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for UserSignedOutByApplicationEvent {EventId}: {UserId}",
                notification.EventId, notification.UserId);
        }
    }
}
