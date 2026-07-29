using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates the destruction-evidence audit entry when staged account
/// destruction finishes. Attributed to the system account with ZERO PII: only
/// the immutable user id (a loose pseudonymous reference, matching the
/// retained AccountDeletionRequests row), the policy version and the external
/// revocation outcome are recorded — never the snapshot email or name the
/// event carries for the final notification.
/// </summary>
public class AccountDeletionCompletedAuditEventHandler : INotificationHandler<AccountDeletionCompletedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<AccountDeletionCompletedAuditEventHandler> _logger;

    public AccountDeletionCompletedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<AccountDeletionCompletedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(AccountDeletionCompletedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "UserManagement",
            action: "user.deletion_completed",
            userId: WellKnownUserIds.System,
            entityType: "User",
            entityId: notification.UserId,
            additionalData:
                $"{{\"policyVersion\":\"{notification.PolicyVersion}\"," +
                $"\"externalRevocationFailed\":{(notification.ExternalRevocationFailed ? "true" : "false")}}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for AccountDeletionCompletedEvent: {UserId}", notification.UserId);
    }
}
