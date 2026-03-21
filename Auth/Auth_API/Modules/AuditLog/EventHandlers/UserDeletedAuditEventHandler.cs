using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a user account is deleted.
/// </summary>
public class UserDeletedAuditEventHandler : INotificationHandler<UserDeletedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserDeletedAuditEventHandler> _logger;

    public UserDeletedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserDeletedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserDeletedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "UserManagement",
            action: "user.deleted",
            userId: notification.DeletedBy,
            entityType: "User",
            entityId: notification.UserId,
            additionalData: $"{{\"email\":\"{notification.Email}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for UserDeletedEvent: {UserId}", notification.UserId);
    }
}
