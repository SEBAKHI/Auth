using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an API key is revoked.
/// </summary>
public class ApiKeyRevokedAuditEventHandler : INotificationHandler<ApiKeyRevokedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<ApiKeyRevokedAuditEventHandler> _logger;

    public ApiKeyRevokedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<ApiKeyRevokedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(ApiKeyRevokedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.ApiKeyManagement,
            action: AuditActions.ApiKeyRevoked,
            performedBy: notification.RevokedBy,
            entityType: "ApiKey",
            entityId: notification.ApiKeyId,
            additionalData: $"{{\"applicationId\":\"{notification.ApplicationId}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for ApiKeyRevokedEvent: {ApiKeyId}", notification.ApiKeyId);
    }
}
