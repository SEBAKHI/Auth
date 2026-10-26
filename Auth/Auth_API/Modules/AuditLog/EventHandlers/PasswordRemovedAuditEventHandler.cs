using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an account's password was removed (OI-215); its
/// details say why: by the owner, or retired when a sign-in by code proved the address
/// for the first time.
/// </summary>
/// <remarks>
/// A separate action from "password.changed": an account becoming code-only is the
/// moment its mailbox becomes its only first factor, and the record has to answer
/// "when did this account stop having a password" on its own. Best-effort: a failure
/// to write the row is logged, never propagated. The change has already committed,
/// and an exception here would turn it into an error response and skip the email to
/// the owner.
/// </remarks>
public class PasswordRemovedAuditEventHandler : INotificationHandler<PasswordRemovedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<PasswordRemovedAuditEventHandler> _logger;

    public PasswordRemovedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<PasswordRemovedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(PasswordRemovedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Security,
                action: AuditActions.PasswordRemoved,
                userId: notification.UserId,
                performedBy: notification.RemovedBy,
                entityType: "User",
                entityId: notification.UserId,
                additionalData: JsonSerializer.Serialize(new { reason = notification.Reason }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug("Audit log created for PasswordRemovedEvent: {UserId}", notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to write the audit row for PasswordRemovedEvent: {UserId}", notification.UserId);
        }
    }
}
