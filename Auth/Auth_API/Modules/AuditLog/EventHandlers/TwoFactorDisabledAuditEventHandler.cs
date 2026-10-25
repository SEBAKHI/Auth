using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when two-factor authentication is disabled.
/// </summary>
/// <remarks>
/// Best-effort: a failure to write the row is logged, never propagated. The change has
/// already committed, and an exception here would turn it into an error response —
/// after S24, before the owner's one notice and the recovery codes the request issued.
/// </remarks>
public class TwoFactorDisabledAuditEventHandler : INotificationHandler<TwoFactorDisabledEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<TwoFactorDisabledAuditEventHandler> _logger;

    public TwoFactorDisabledAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<TwoFactorDisabledAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(TwoFactorDisabledEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Security,
                action: AuditActions.TwoFactorDisabled,
                userId: notification.UserId,
                performedBy: notification.DisabledBy,
                entityType: "User",
                entityId: notification.UserId);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug("Audit log created for TwoFactorDisabledEvent: {UserId}", notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to write the audit row for TwoFactorDisabledEvent: {UserId}", notification.UserId);
        }
    }
}
