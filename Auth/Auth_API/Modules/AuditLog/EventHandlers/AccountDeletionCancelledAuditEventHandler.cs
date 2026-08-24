using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a user recovers their account during the
/// grace window, cancelling the pending deletion.
/// </summary>
public class AccountDeletionCancelledAuditEventHandler : INotificationHandler<AccountDeletionCancelledEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<AccountDeletionCancelledAuditEventHandler> _logger;

    public AccountDeletionCancelledAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<AccountDeletionCancelledAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(AccountDeletionCancelledEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "UserManagement",
            action: "user.deletion_cancelled",
            userId: notification.UserId,
            performedBy: notification.UserId,
            entityType: "User",
            entityId: notification.UserId,
            additionalData: $"{{\"cancelledAtUtc\":\"{notification.CancelledAtUtc:O}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for AccountDeletionCancelledEvent: {UserId}", notification.UserId);
    }
}
