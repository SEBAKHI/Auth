using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a user requests deletion of their account.
/// The account's own audit rows are anonymized at destruction time, so no PII
/// beyond the loose user reference is recorded here.
/// </summary>
public class AccountDeletionRequestedAuditEventHandler : INotificationHandler<AccountDeletionRequestedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<AccountDeletionRequestedAuditEventHandler> _logger;

    public AccountDeletionRequestedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<AccountDeletionRequestedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(AccountDeletionRequestedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "UserManagement",
            action: "user.deletion_requested",
            userId: notification.UserId,
            performedBy: notification.UserId,
            entityType: "User",
            entityId: notification.UserId,
            additionalData:
                $"{{\"source\":\"{notification.Source}\",\"graceEndsAtUtc\":\"{notification.GraceEndsAtUtc:O}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for AccountDeletionRequestedEvent: {UserId}", notification.UserId);
    }
}
