using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an application's two-step verification requirement is
/// switched on or off (S23, AM11), at creation or at an update. Who may sign in depends on
/// it, so it gets its own line rather than being buried in a settings diff.
/// </summary>
/// <remarks>
/// Best-effort, like its siblings for the redirect allowlists and the launch addresses: the
/// save has already committed, so a failure here is logged and never propagated. No user is
/// the subject: the row names the application and the administrator.
/// </remarks>
public class ApplicationTwoFactorRequirementChangedAuditEventHandler
    : INotificationHandler<ApplicationTwoFactorRequirementChangedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<ApplicationTwoFactorRequirementChangedAuditEventHandler> _logger;

    public ApplicationTwoFactorRequirementChangedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<ApplicationTwoFactorRequirementChangedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(ApplicationTwoFactorRequirementChangedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Application,
                action: AuditActions.ApplicationTwoFactorRequirementChanged,
                performedBy: notification.TriggeredBy,
                applicationId: notification.ApplicationId,
                entityType: "Application",
                entityId: notification.ApplicationId,
                additionalData: JsonSerializer.Serialize(new
                {
                    applicationCode = notification.ApplicationCode,
                    requireTwoFactor = notification.RequireTwoFactor
                }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for ApplicationTwoFactorRequirementChangedEvent {EventId}: application {ApplicationId}, RequireTwoFactor {RequireTwoFactor}",
                notification.EventId, notification.ApplicationId, notification.RequireTwoFactor);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for ApplicationTwoFactorRequirementChangedEvent {EventId}: application {ApplicationId}",
                notification.EventId, notification.ApplicationId);
        }
    }
}
