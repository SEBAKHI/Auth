using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records every session the concurrent-session limit ended.
///
/// One row per session, not one per event: the trail exists so an operator can
/// answer "what happened to this session", and that question is asked about a
/// specific session id. The email is the summary; this is the ledger.
/// </summary>
public class SessionLimitEnforcedAuditEventHandler : INotificationHandler<SessionLimitEnforcedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<SessionLimitEnforcedAuditEventHandler> _logger;

    public SessionLimitEnforcedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<SessionLimitEnforcedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(SessionLimitEnforcedEvent notification, CancellationToken cancellationToken)
    {
        foreach (var ended in notification.EndedSessions)
        {
            // 'session.ended' is the action name the AuditLogs DDL already
            // reserves for this entity type.
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: "Security",
                action: "session.ended",
                userId: notification.UserId,
            performedBy: WellKnownUserIds.System,
                entityType: "Session",
                entityId: ended.SessionId);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
        }

        _logger.LogDebug(
            "Audit logs created for SessionLimitEnforcedEvent: {SessionCount} sessions for user {UserId}",
            notification.EndedSessions.Count, notification.UserId);
    }
}
