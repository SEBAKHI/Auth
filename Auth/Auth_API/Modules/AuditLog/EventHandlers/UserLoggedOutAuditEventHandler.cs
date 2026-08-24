using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a user logs out.
/// </summary>
public class UserLoggedOutAuditEventHandler : INotificationHandler<UserLoggedOutEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserLoggedOutAuditEventHandler> _logger;

    public UserLoggedOutAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserLoggedOutAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserLoggedOutEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Authentication",
            action: notification.AllDevices ? "user.logout.all" : "user.logout",
            userId: notification.UserId,
            performedBy: notification.UserId,
            entityType: "User",
            entityId: notification.UserId);

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for UserLoggedOutEvent: {UserId}", notification.UserId);
    }
}
