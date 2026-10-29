using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an API key was rotated (OI-128 PR B): a new credential was
/// issued with the old one's scopes. Critical by nature (a credential was issued), written
/// best-effort like every audit handler here until the durable publisher exists: a failure is
/// logged, never propagated.
/// </summary>
/// <remarks>
/// The row names the new key and the one it replaces, never their material. The details are
/// serialized, not interpolated.
/// </remarks>
public class ApiKeyRotatedAuditEventHandler : INotificationHandler<ApiKeyRotatedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<ApiKeyRotatedAuditEventHandler> _logger;

    public ApiKeyRotatedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<ApiKeyRotatedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(ApiKeyRotatedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.ApiKeyManagement,
                action: AuditActions.ApiKeyRotated,
                performedBy: notification.TriggeredBy,
                applicationId: notification.ApplicationId,
                entityType: "ApiKey",
                entityId: notification.ApiKeyId,
                additionalData: JsonSerializer.Serialize(new
                {
                    replacedApiKeyId = notification.ReplacedApiKeyId,
                    replacedKeyExpiresAt = notification.ReplacedKeyExpiresAt
                }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for ApiKeyRotatedEvent {EventId}: {ApiKeyId}",
                notification.EventId, notification.ApiKeyId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for ApiKeyRotatedEvent {EventId}: {ApiKeyId}",
                notification.EventId, notification.ApiKeyId);
        }
    }
}
