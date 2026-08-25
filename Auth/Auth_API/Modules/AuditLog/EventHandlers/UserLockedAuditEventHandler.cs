using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a user account is locked.
/// </summary>
public class UserLockedAuditEventHandler : INotificationHandler<UserLockedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserLockedAuditEventHandler> _logger;

    public UserLockedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserLockedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserLockedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Security,
            action: AuditActions.UserLocked,
            userId: notification.UserId,
            performedBy: notification.LockedBy,
            entityType: "User",
            entityId: notification.UserId,
            additionalData: $"{{\"lockoutEnd\":\"{notification.LockoutEnd?.ToString("o") ?? "indefinite"}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for UserLockedEvent: {UserId}", notification.UserId);
    }
}
