using System.Text.Json;
using Auth.Application.Common;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Writes the one audit row a registration attempt leaves before an account
/// exists. Under verify-first registration an abandoned or abusive attempt
/// creates no Users row and therefore no UserCreated row; this is what lets
/// those attempts be seen at all. No user id (there is no user), the actor is
/// the anonymous principal, and the address is stored masked — an audit row
/// is not the place to keep a stranger's typed address in the clear.
/// </summary>
public class RegistrationStartedAuditEventHandler : INotificationHandler<RegistrationStartedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<RegistrationStartedAuditEventHandler> _logger;

    public RegistrationStartedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<RegistrationStartedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(RegistrationStartedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Authentication,
            action: AuditActions.RegistrationStarted,
            userId: null,
            performedBy: Guid.Empty,
            entityType: "PendingRegistration",
            entityId: notification.PendingRegistrationId,
            ipAddress: notification.IpAddress,
            userAgent: notification.UserAgent,
            additionalData: JsonSerializer.Serialize(new { email = EmailMasking.Mask(notification.Email) }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug(
            "Audit log created for RegistrationStartedEvent: {PendingRegistrationId}",
            notification.PendingRegistrationId);
    }
}
