using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates a failure row of the proof's own action when the first secret of a proof inside a
/// session was right and its second step was refused (REVIEW-FIXES 152 F25): the session, the
/// refusal's published code, and where it came from. Never the code that was given.
/// </summary>
/// <remarks>
/// Best-effort: a failure to write the row is logged, never propagated. The refusal is already the
/// answer, and an exception here would turn it into a server error.
/// </remarks>
public class UserReauthenticationFailedAuditEventHandler : INotificationHandler<UserReauthenticationFailedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserReauthenticationFailedAuditEventHandler> _logger;

    public UserReauthenticationFailedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserReauthenticationFailedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserReauthenticationFailedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateFailure(
                actionType: AuditActionTypes.Security,
                action: AuditActions.SessionReauthenticated,
                errorMessage: notification.Reason,
                userId: notification.UserId,
                performedBy: notification.UserId,
                entityType: "User",
                entityId: notification.UserId,
                ipAddress: notification.IpAddress,
                userAgent: notification.UserAgent,
                sessionId: notification.SessionId);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug("Audit log created for UserReauthenticationFailedEvent: {UserId}", notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to write the audit row for UserReauthenticationFailedEvent: {UserId}", notification.UserId);
        }
    }
}
