using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a signed-in person proves themself again inside a session: the
/// session, and what the proof proved, as a sign-in's row says it. Never the proof itself.
/// </summary>
/// <remarks>
/// Best-effort: a failure to write the row is logged, never propagated. The session is already
/// stamped, and an exception here would turn the proof into an error response.
/// </remarks>
public class UserReauthenticatedAuditEventHandler : INotificationHandler<UserReauthenticatedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserReauthenticatedAuditEventHandler> _logger;

    public UserReauthenticatedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserReauthenticatedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserReauthenticatedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Security,
                action: AuditActions.SessionReauthenticated,
                userId: notification.UserId,
                performedBy: notification.UserId,
                entityType: "User",
                entityId: notification.UserId,
                additionalData: JsonSerializer.Serialize(new
                {
                    methods = notification.Methods.ToStored(),
                    amr = notification.Methods.ToAmrValues(),
                }),
                sessionId: notification.SessionId);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug("Audit log created for UserReauthenticatedEvent: {UserId}", notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to write the audit row for UserReauthenticatedEvent: {UserId}", notification.UserId);
        }
    }
}
