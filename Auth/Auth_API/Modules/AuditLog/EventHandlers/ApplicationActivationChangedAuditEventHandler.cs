using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an application is switched on or off.
/// Deactivation locks out every user at once, so it gets its own line rather
/// than being buried in a settings diff.
/// </summary>
public class ApplicationActivationChangedAuditEventHandler : INotificationHandler<ApplicationActivationChangedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<ApplicationActivationChangedAuditEventHandler> _logger;

    public ApplicationActivationChangedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<ApplicationActivationChangedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(ApplicationActivationChangedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Application,
            action: notification.IsActive ? AuditActions.ApplicationActivated : AuditActions.ApplicationDeactivated,
            performedBy: notification.ChangedBy,
            entityType: "Application",
            entityId: notification.ApplicationId,
            additionalData: $"{{\"applicationCode\":\"{notification.ApplicationCode}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug(
            "Audit log created for ApplicationActivationChangedEvent: Application {ApplicationId}, IsActive {IsActive}",
            notification.ApplicationId, notification.IsActive);
    }
}
