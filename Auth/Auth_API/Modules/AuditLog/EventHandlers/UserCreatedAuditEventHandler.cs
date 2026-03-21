using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a new user is created.
/// </summary>
public class UserCreatedAuditEventHandler : INotificationHandler<UserCreatedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserCreatedAuditEventHandler> _logger;

    public UserCreatedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserCreatedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserCreatedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "UserManagement",
            action: "user.created",
            userId: notification.CreatedBy,
            entityType: "User",
            entityId: notification.UserId,
            additionalData: $"{{\"email\":\"{notification.Email}\",\"firstName\":\"{notification.FirstName}\",\"lastName\":\"{notification.LastName}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for UserCreatedEvent: {UserId}", notification.UserId);
    }
}
