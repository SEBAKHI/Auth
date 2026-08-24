using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when two-factor authentication is enabled.
/// </summary>
public class TwoFactorEnabledAuditEventHandler : INotificationHandler<TwoFactorEnabledEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<TwoFactorEnabledAuditEventHandler> _logger;

    public TwoFactorEnabledAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<TwoFactorEnabledAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(TwoFactorEnabledEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Security",
            action: "twofactor.enabled",
            userId: notification.UserId,
            performedBy: notification.EnabledBy,
            entityType: "User",
            entityId: notification.UserId);

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for TwoFactorEnabledEvent: {UserId}", notification.UserId);
    }
}
