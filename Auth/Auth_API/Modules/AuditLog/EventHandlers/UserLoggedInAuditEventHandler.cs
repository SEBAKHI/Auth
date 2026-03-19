using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Features.Authentication.Login;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a user logs in.
/// </summary>
public class UserLoggedInAuditEventHandler : INotificationHandler<UserLoggedInEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserLoggedInAuditEventHandler> _logger;

    public UserLoggedInAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserLoggedInAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserLoggedInEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Authentication",
            action: "user.login",
            userId: notification.UserId,
            entityType: "User",
            entityId: notification.UserId,
            ipAddress: notification.IpAddress,
            userAgent: notification.UserAgent);

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for UserLoggedInEvent: {UserId}", notification.UserId);
    }
}
