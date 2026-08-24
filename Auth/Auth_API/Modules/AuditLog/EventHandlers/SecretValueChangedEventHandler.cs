using System.Text.Json;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Records that a stored secret value was replaced outside the challenge-gated
/// rotation flow.
/// </summary>
/// <remarks>
/// The rotation operations leave their trail through
/// <see cref="SecretOperationExecutedAuditEventHandler"/>, which anchors each row
/// to the confirmation that was spent. These operations have no confirmation to
/// anchor to, so without this handler repointing the API at another database
/// would be visible only in application logs. Only the key name is written —
/// never the value, and never a digest of it.
/// </remarks>
public class SecretValueChangedEventHandler
    : INotificationHandler<SecretValueChangedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<SecretValueChangedEventHandler> _logger;

    public SecretValueChangedEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<SecretValueChangedEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(
        SecretValueChangedEvent notification,
        CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Administration",
            action: "secrets.value.changed",
            performedBy: notification.ChangedBy,
            entityType: "Secret",
            newValues: JsonSerializer.Serialize(new
            {
                secretKey = notification.SecretKey
            }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);

        _logger.LogDebug(
            "Audit log created for SecretValueChangedEvent ({SecretKey}) by {ChangedBy}",
            notification.SecretKey, notification.ChangedBy);
    }
}
