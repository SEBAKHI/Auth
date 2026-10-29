using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a sign-in page design was created, from scratch or as a copy. It starts inactive, so the row records what was made, not a change visitors see.
/// </summary>
/// <remarks>
/// Best-effort, like its siblings for the application's appearance and addresses: the change
/// has already committed, so a failure here is logged and never propagated. The row carries the
/// design's name, logo choice and content hash, never its content.
/// </remarks>
public class LoginDesignCreatedAuditEventHandler : INotificationHandler<LoginDesignCreatedEvent>
{
    private static readonly JsonSerializerOptions Camel = new(JsonSerializerDefaults.Web);

    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<LoginDesignCreatedAuditEventHandler> _logger;

    public LoginDesignCreatedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<LoginDesignCreatedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(LoginDesignCreatedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Administration,
                action: AuditActions.LoginDesignCreated,
                performedBy: notification.TriggeredBy,
                applicationId: notification.ApplicationId,
                entityType: "LoginDesign",
                entityId: notification.LoginDesignId,
                oldValues: null,
                newValues: JsonSerializer.Serialize(notification.Design, Camel),
                additionalData: JsonSerializer.Serialize(new { copiedFromId = notification.CopiedFromId }, Camel));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for LoginDesignCreatedEvent {EventId}: design {DesignId}",
                notification.EventId, notification.LoginDesignId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for LoginDesignCreatedEvent {EventId}: design {DesignId}",
                notification.EventId, notification.LoginDesignId);
        }
    }
}
