using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a user's password is changed.
/// </summary>
public class PasswordChangedAuditEventHandler : INotificationHandler<PasswordChangedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<PasswordChangedAuditEventHandler> _logger;

    public PasswordChangedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<PasswordChangedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(PasswordChangedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Security,
            action: AuditActions.PasswordChanged,
            userId: notification.UserId,
            performedBy: notification.ChangedBy,
            entityType: "User",
            entityId: notification.UserId);

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for PasswordChangedEvent: {UserId}", notification.UserId);
    }
}
