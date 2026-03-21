using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a user account is unlocked.
/// </summary>
public class UserUnlockedAuditEventHandler : INotificationHandler<UserUnlockedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserUnlockedAuditEventHandler> _logger;

    public UserUnlockedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserUnlockedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserUnlockedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Security",
            action: "user.unlocked",
            userId: notification.UnlockedBy,
            entityType: "User",
            entityId: notification.UserId);

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for UserUnlockedEvent: {UserId}", notification.UserId);
    }
}
