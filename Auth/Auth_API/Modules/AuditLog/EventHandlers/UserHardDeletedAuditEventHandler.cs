using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a user account is permanently deleted.
/// The purge removed the account's own audit trail, so this entry — attributed
/// to the administrator who performed the deletion — is the tombstone that
/// records the destruction.
/// </summary>
public class UserHardDeletedAuditEventHandler : INotificationHandler<UserHardDeletedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserHardDeletedAuditEventHandler> _logger;

    public UserHardDeletedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserHardDeletedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserHardDeletedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "UserManagement",
            action: "user.harddeleted",
            // No userId on purpose, and this is the one handler where that is
            // not an omission. The purge anonymises every AuditLogs row whose
            // UserId is the deleted account, so a tombstone naming it there
            // would either be scrubbed with the rest or, if written afterwards,
            // survive as the one row that defeats the erasure. The account stays
            // identified by EntityId, and the administrator by performedBy.
            performedBy: notification.DeletedBy,
            entityType: "User",
            entityId: notification.UserId,
            additionalData: $"{{\"email\":\"{notification.Email}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for UserHardDeletedEvent: {UserId}", notification.UserId);
    }
}
