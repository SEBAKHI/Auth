using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when two-factor authentication is enabled.
/// </summary>
/// <remarks>
/// Best-effort: a failure to write the row is logged, never propagated. The change has
/// already committed, and an exception here would turn it into an error response —
/// after S24, before the owner's one notice and the recovery codes the request issued.
/// </remarks>
public class TwoFactorEnabledAuditEventHandler : INotificationHandler<TwoFactorEnabledEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<TwoFactorEnabledAuditEventHandler> _logger;

    public TwoFactorEnabledAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<TwoFactorEnabledAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(TwoFactorEnabledEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Security,
                action: AuditActions.TwoFactorEnabled,
                userId: notification.UserId,
                performedBy: notification.EnabledBy,
                entityType: "User",
                entityId: notification.UserId);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug("Audit log created for TwoFactorEnabledEvent: {UserId}", notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to write the audit row for TwoFactorEnabledEvent: {UserId}", notification.UserId);
        }
    }
}
