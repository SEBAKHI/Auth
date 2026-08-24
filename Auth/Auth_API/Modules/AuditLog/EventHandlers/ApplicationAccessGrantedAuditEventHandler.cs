using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a user is invited to an application.
/// </summary>
public class ApplicationAccessGrantedAuditEventHandler : INotificationHandler<ApplicationAccessGrantedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<ApplicationAccessGrantedAuditEventHandler> _logger;

    public ApplicationAccessGrantedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<ApplicationAccessGrantedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(ApplicationAccessGrantedEvent notification, CancellationToken cancellationToken)
    {
        var expiresAt = notification.ExpiresAt?.ToString("O") ?? "null";

        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Application",
            action: "application.access.granted",
            userId: notification.UserId,
            performedBy: notification.GrantedBy,
            entityType: "ApplicationUserAccess",
            entityId: notification.ApplicationId,
            additionalData:
                $"{{\"applicationCode\":\"{notification.ApplicationCode}\"," +
                $"\"userId\":\"{notification.UserId}\"," +
                $"\"expiresAt\":\"{expiresAt}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug(
            "Audit log created for ApplicationAccessGrantedEvent: Application {ApplicationId}, User {UserId}",
            notification.ApplicationId, notification.UserId);
    }
}
