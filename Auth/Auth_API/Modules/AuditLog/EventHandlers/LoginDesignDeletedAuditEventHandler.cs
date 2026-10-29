using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a sign-in page design was deleted (only an inactive design can be). The row keeps its name and version, the only trace left of it.
/// </summary>
/// <remarks>
/// Best-effort, like its siblings for the application's appearance and addresses: the change
/// has already committed, so a failure here is logged and never propagated. The row carries the
/// design's name, logo choice and content hash, never its content.
/// </remarks>
public class LoginDesignDeletedAuditEventHandler : INotificationHandler<LoginDesignDeletedEvent>
{
    private static readonly JsonSerializerOptions Camel = new(JsonSerializerDefaults.Web);

    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<LoginDesignDeletedAuditEventHandler> _logger;

    public LoginDesignDeletedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<LoginDesignDeletedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(LoginDesignDeletedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Administration,
                action: AuditActions.LoginDesignDeleted,
                performedBy: notification.TriggeredBy,
                applicationId: notification.ApplicationId,
                entityType: "LoginDesign",
                entityId: notification.LoginDesignId,
                oldValues: JsonSerializer.Serialize(notification.Design, Camel),
                newValues: null,
                additionalData: null);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for LoginDesignDeletedEvent {EventId}: design {DesignId}",
                notification.EventId, notification.LoginDesignId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for LoginDesignDeletedEvent {EventId}: design {DesignId}",
                notification.EventId, notification.LoginDesignId);
        }
    }
}
