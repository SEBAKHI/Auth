using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an account gains its FIRST password.
/// </summary>
/// <remarks>
/// A separate action from "password.changed" on purpose. An external-only account
/// (Google, Apple) acquiring local credentials is the moment it becomes reachable by a
/// second means, so the record has to answer "when did this account first get a password"
/// without that question being confused with a routine rotation.
/// </remarks>
public class PasswordCreatedAuditEventHandler : INotificationHandler<PasswordCreatedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<PasswordCreatedAuditEventHandler> _logger;

    public PasswordCreatedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<PasswordCreatedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(PasswordCreatedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Security",
            action: "password.created",
            userId: notification.SetBy,
            entityType: "User",
            entityId: notification.UserId);

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for PasswordCreatedEvent: {UserId}", notification.UserId);
    }
}
