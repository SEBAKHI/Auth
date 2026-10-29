using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a sign-in page design was activated: from now on the scope's sign-in pages show it. The row names the design it replaced, deactivated in the same transaction.
/// </summary>
/// <remarks>
/// Best-effort, like its siblings for the application's appearance and addresses: the change
/// has already committed, so a failure here is logged and never propagated. The row carries the
/// design's name, logo choice and content hash, never its content.
/// </remarks>
public class LoginDesignActivatedAuditEventHandler : INotificationHandler<LoginDesignActivatedEvent>
{
    private static readonly JsonSerializerOptions Camel = new(JsonSerializerDefaults.Web);

    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<LoginDesignActivatedAuditEventHandler> _logger;

    public LoginDesignActivatedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<LoginDesignActivatedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(LoginDesignActivatedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Administration,
                action: AuditActions.LoginDesignActivated,
                performedBy: notification.TriggeredBy,
                applicationId: notification.ApplicationId,
                entityType: "LoginDesign",
                entityId: notification.LoginDesignId,
                oldValues: null,
                newValues: JsonSerializer.Serialize(notification.Design, Camel),
                additionalData: JsonSerializer.Serialize(new { previousActiveId = notification.PreviousActiveId }, Camel));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for LoginDesignActivatedEvent {EventId}: design {DesignId}",
                notification.EventId, notification.LoginDesignId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for LoginDesignActivatedEvent {EventId}: design {DesignId}",
                notification.EventId, notification.LoginDesignId);
        }
    }
}
