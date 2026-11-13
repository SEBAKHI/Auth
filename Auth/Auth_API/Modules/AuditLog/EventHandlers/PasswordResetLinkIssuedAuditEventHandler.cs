using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a signed-in person had a password reset link sent to an
/// account's address (S13): the account is the subject, whoever asked is the performer.
/// </summary>
/// <remarks>
/// Best-effort, by the rule the newer audit handlers follow: a failure to write the row is logged
/// once and never propagated. The link has already been issued and mailed, and an exception here
/// would turn that into an error response. Runs once per publish (inline dispatch).
/// </remarks>
public class PasswordResetLinkIssuedAuditEventHandler : INotificationHandler<PasswordResetLinkIssuedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<PasswordResetLinkIssuedAuditEventHandler> _logger;

    public PasswordResetLinkIssuedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<PasswordResetLinkIssuedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(PasswordResetLinkIssuedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Security,
                action: AuditActions.PasswordResetLinkIssued,
                userId: notification.UserId,
                performedBy: notification.TriggeredBy,
                entityType: "User",
                entityId: notification.UserId);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for PasswordResetLinkIssuedEvent {EventId}: {UserId}", notification.EventId, notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Failed to write the audit row for PasswordResetLinkIssuedEvent {EventId}: {UserId}",
                notification.EventId, notification.UserId);
        }
    }
}
