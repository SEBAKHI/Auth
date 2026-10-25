using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when the emailed code became an account's second factor
/// (S24); its details say whether it took the place of an authenticator app.
/// </summary>
/// <remarks>
/// Best-effort: a failure to write the row is logged, never propagated. The change has
/// already committed, and an exception here would turn it into an error response and
/// skip the email to the owner.
/// </remarks>
public class TwoFactorEmailCodeEnabledAuditEventHandler : INotificationHandler<TwoFactorEmailCodeEnabledEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<TwoFactorEmailCodeEnabledAuditEventHandler> _logger;

    public TwoFactorEmailCodeEnabledAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<TwoFactorEmailCodeEnabledAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(TwoFactorEmailCodeEnabledEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Security,
                action: AuditActions.TwoFactorEmailCodeEnabled,
                userId: notification.UserId,
                performedBy: notification.EnabledBy,
                entityType: "User",
                entityId: notification.UserId,
                additionalData: JsonSerializer.Serialize(new
                {
                    replacedAuthenticator = notification.ReplacedAuthenticator
                }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug("Audit log created for TwoFactorEmailCodeEnabledEvent: {UserId}", notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to write the audit row for TwoFactorEmailCodeEnabledEvent: {UserId}", notification.UserId);
        }
    }
}
