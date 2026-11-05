using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a passkey is added to an account (S20).
/// </summary>
/// <remarks>
/// Best-effort: a failure to write the row is logged, never propagated. The passkey is already
/// saved, and an exception here would turn the completed registration into an error response
/// and skip the owner's notice. The details are built with System.Text.Json (ARR-1058), never
/// by interpolation: the name is the owner's own text. No credential id, public key or user
/// handle is recorded (PK16).
/// </remarks>
public sealed class PasskeyRegisteredAuditEventHandler : INotificationHandler<PasskeyRegisteredEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<PasskeyRegisteredAuditEventHandler> _logger;

    public PasskeyRegisteredAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<PasskeyRegisteredAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(PasskeyRegisteredEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Security,
                action: AuditActions.PasskeyRegistered,
                userId: notification.UserId,
                performedBy: notification.RegisteredBy,
                entityType: "UserPasskey",
                entityId: notification.PasskeyId,
                additionalData: JsonSerializer.Serialize(new { name = notification.Name }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for PasskeyRegisteredEvent: passkey {PasskeyId} of user {UserId}",
                notification.PasskeyId, notification.UserId);
        }
    }
}
