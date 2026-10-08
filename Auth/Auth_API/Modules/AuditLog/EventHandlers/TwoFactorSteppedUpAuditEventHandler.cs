using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a signed-in session proves the second factor
/// (a step-up): the session, and which factor proved it — never the code.
/// </summary>
/// <remarks>
/// A failure to write the row is logged, never propagated: the session is already
/// upgraded, and an exception here would turn the step-up into an error response.
/// </remarks>
public class TwoFactorSteppedUpAuditEventHandler : INotificationHandler<TwoFactorSteppedUpEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<TwoFactorSteppedUpAuditEventHandler> _logger;

    public TwoFactorSteppedUpAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<TwoFactorSteppedUpAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(TwoFactorSteppedUpEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Security,
                action: AuditActions.TwoFactorSteppedUp,
                userId: notification.UserId,
                performedBy: notification.UserId,
                entityType: "User",
                entityId: notification.UserId,
                additionalData: JsonSerializer.Serialize(new { method = notification.Method.ToString() }),
                sessionId: notification.SessionId);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug("Audit log created for TwoFactorSteppedUpEvent: {UserId}", notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to write the audit row for TwoFactorSteppedUpEvent: {UserId}", notification.UserId);
        }
    }
}
