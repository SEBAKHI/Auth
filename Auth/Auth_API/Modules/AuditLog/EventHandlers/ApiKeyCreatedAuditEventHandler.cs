using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an API key is created.
/// </summary>
public class ApiKeyCreatedAuditEventHandler : INotificationHandler<ApiKeyCreatedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<ApiKeyCreatedAuditEventHandler> _logger;

    public ApiKeyCreatedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<ApiKeyCreatedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(ApiKeyCreatedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.ApiKeyManagement,
            action: AuditActions.ApiKeyCreated,
            performedBy: notification.CreatedBy,
            entityType: "ApiKey",
            entityId: notification.ApiKeyId,
            additionalData: $"{{\"applicationId\":\"{notification.ApplicationId}\",\"name\":\"{notification.Name}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for ApiKeyCreatedEvent: {ApiKeyId}", notification.ApiKeyId);
    }
}
