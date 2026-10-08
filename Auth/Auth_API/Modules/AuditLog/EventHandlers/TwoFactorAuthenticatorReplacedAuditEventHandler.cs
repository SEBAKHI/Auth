using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a user moves their second factor to a new authenticator app.
/// </summary>
/// <remarks>
/// A failure to write the row is logged, never propagated: the change has already
/// committed, and an exception here would turn it into an error response and skip
/// the email to the owner.
/// </remarks>
public class TwoFactorAuthenticatorReplacedAuditEventHandler : INotificationHandler<TwoFactorAuthenticatorReplacedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<TwoFactorAuthenticatorReplacedAuditEventHandler> _logger;

    public TwoFactorAuthenticatorReplacedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<TwoFactorAuthenticatorReplacedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(TwoFactorAuthenticatorReplacedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Security,
                action: AuditActions.TwoFactorAuthenticatorReplaced,
                userId: notification.UserId,
                performedBy: notification.ReplacedBy,
                entityType: "User",
                entityId: notification.UserId);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug("Audit log created for TwoFactorAuthenticatorReplacedEvent: {UserId}", notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to write the audit row for TwoFactorAuthenticatorReplacedEvent: {UserId}", notification.UserId);
        }
    }
}
