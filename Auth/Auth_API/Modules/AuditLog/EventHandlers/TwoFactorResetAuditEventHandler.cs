using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an administrator removes another account's second factor:
/// <c>UserId</c> is that account, <c>PerformedBy</c> the administrator.
/// </summary>
/// <remarks>
/// A failure to write the row is logged, never propagated: the change has already
/// committed, and an exception here would turn it into an error response and skip
/// the email to the owner.
/// </remarks>
public class TwoFactorResetAuditEventHandler : INotificationHandler<TwoFactorResetEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<TwoFactorResetAuditEventHandler> _logger;

    public TwoFactorResetAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<TwoFactorResetAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(TwoFactorResetEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Security,
                action: AuditActions.TwoFactorResetByAdministrator,
                userId: notification.UserId,
                performedBy: notification.ResetBy,
                entityType: "User",
                entityId: notification.UserId);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug("Audit log created for TwoFactorResetEvent: {UserId}", notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to write the audit row for TwoFactorResetEvent: {UserId}", notification.UserId);
        }
    }
}
