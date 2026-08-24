using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records the moment an external provider identity was attached to an existing account.
/// </summary>
/// <remarks>
/// A detective control, not a preventive one. Linking still requires no consent and sends no
/// notification; what changes is that it stops being invisible. The warning level when the
/// account carries a wildcard permission is the point of the whole handler: linking a Google
/// account to an ordinary user is routine, and linking one to an account that can do anything
/// is the event somebody should see the same day it happens.
/// </remarks>
public class ExternalProviderLinkedAuditEventHandler : INotificationHandler<ExternalProviderLinkedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<ExternalProviderLinkedAuditEventHandler> _logger;

    public ExternalProviderLinkedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<ExternalProviderLinkedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(ExternalProviderLinkedEvent notification, CancellationToken cancellationToken)
    {
        // Serialized rather than interpolated like its neighbours: the provider code reaches
        // this from the request body, and a hand-built JSON string would be one stray quote
        // away from an unparseable audit row.
        var additionalData = JsonSerializer.Serialize(new
        {
            provider = notification.Provider,
            providerUserId = notification.ProviderUserId,
            holdsWildcardPermission = notification.HoldsWildcardPermission,
        });

        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Security",
            action: "external-login.linked",
            userId: notification.UserId,
            performedBy: notification.UserId,
            entityType: "User",
            entityId: notification.UserId,
            additionalData: additionalData);

        await _auditLogRepository.CreateAsync(log, cancellationToken);

        if (notification.HoldsWildcardPermission)
        {
            _logger.LogWarning(
                "{Provider} was linked to user {UserId}, which holds a wildcard permission. "
                + "Whoever controls that provider account can now sign in as this one.",
                notification.Provider, notification.UserId);
        }
        else
        {
            _logger.LogInformation(
                "Audit log created for ExternalProviderLinkedEvent: {UserId} ({Provider})",
                notification.UserId, notification.Provider);
        }
    }
}
