using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a user replaces the recovery codes of their second factor.
/// </summary>
/// <remarks>
/// A failure to write the row is logged, never propagated: the change has already
/// committed, and an exception here would turn it into an error response and skip
/// the email to the owner.
/// </remarks>
public class TwoFactorRecoveryCodesRegeneratedAuditEventHandler : INotificationHandler<TwoFactorRecoveryCodesRegeneratedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<TwoFactorRecoveryCodesRegeneratedAuditEventHandler> _logger;

    public TwoFactorRecoveryCodesRegeneratedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<TwoFactorRecoveryCodesRegeneratedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(TwoFactorRecoveryCodesRegeneratedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Security,
                action: AuditActions.TwoFactorRecoveryCodesRegenerated,
                userId: notification.UserId,
                performedBy: notification.RegeneratedBy,
                entityType: "User",
                entityId: notification.UserId);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug("Audit log created for TwoFactorRecoveryCodesRegeneratedEvent: {UserId}", notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to write the audit row for TwoFactorRecoveryCodesRegeneratedEvent: {UserId}", notification.UserId);
        }
    }
}
