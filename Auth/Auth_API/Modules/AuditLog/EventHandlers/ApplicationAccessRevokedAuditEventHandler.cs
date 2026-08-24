using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a user's invitation to an application is
/// withdrawn.
/// </summary>
public class ApplicationAccessRevokedAuditEventHandler : INotificationHandler<ApplicationAccessRevokedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<ApplicationAccessRevokedAuditEventHandler> _logger;

    public ApplicationAccessRevokedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<ApplicationAccessRevokedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(ApplicationAccessRevokedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Application",
            action: "application.access.revoked",
            userId: notification.UserId,
            performedBy: notification.RevokedBy,
            entityType: "ApplicationUserAccess",
            entityId: notification.ApplicationId,
            additionalData:
                $"{{\"applicationCode\":\"{notification.ApplicationCode}\"," +
                $"\"userId\":\"{notification.UserId}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug(
            "Audit log created for ApplicationAccessRevokedEvent: Application {ApplicationId}, User {UserId}",
            notification.ApplicationId, notification.UserId);
    }
}
