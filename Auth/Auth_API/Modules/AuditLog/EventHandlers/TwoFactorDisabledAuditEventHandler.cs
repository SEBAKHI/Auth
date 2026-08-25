using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when two-factor authentication is disabled.
/// </summary>
public class TwoFactorDisabledAuditEventHandler : INotificationHandler<TwoFactorDisabledEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<TwoFactorDisabledAuditEventHandler> _logger;

    public TwoFactorDisabledAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<TwoFactorDisabledAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(TwoFactorDisabledEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Security,
            action: AuditActions.TwoFactorDisabled,
            userId: notification.UserId,
            performedBy: notification.DisabledBy,
            entityType: "User",
            entityId: notification.UserId);

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for TwoFactorDisabledEvent: {UserId}", notification.UserId);
    }
}
