using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an application's redirect allowlists changed (OI-171, folding
/// OI-166 (c)): the public list, the server-side list, or the post-logout list, each with what it
/// held before and after.
/// </summary>
/// <remarks>
/// Best-effort, like every audit handler written since the event rules were recorded: the save has
/// already committed, so a failure here is logged and never propagated. A handler that threw would
/// turn a saved application into an error answer (repository instructions, section on events).
/// The details are serialized, not interpolated: the addresses are an administrator's free text,
/// and none of it can break the JSON.
/// </remarks>
public class ApplicationRedirectUrisChangedAuditEventHandler : INotificationHandler<ApplicationRedirectUrisChangedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<ApplicationRedirectUrisChangedAuditEventHandler> _logger;

    public ApplicationRedirectUrisChangedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<ApplicationRedirectUrisChangedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(ApplicationRedirectUrisChangedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Application,
                action: AuditActions.ApplicationRedirectUrisChanged,
                performedBy: notification.TriggeredBy,
                applicationId: notification.ApplicationId,
                entityType: "Application",
                entityId: notification.ApplicationId,
                additionalData: JsonSerializer.Serialize(new
                {
                    applicationCode = notification.ApplicationCode,
                    redirectUris = notification.RedirectUris,
                    confidentialRedirectUris = notification.ConfidentialRedirectUris,
                    postLogoutRedirectUris = notification.PostLogoutRedirectUris
                }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for ApplicationRedirectUrisChangedEvent {EventId}: application {ApplicationId}",
                notification.EventId, notification.ApplicationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for ApplicationRedirectUrisChangedEvent {EventId}: application {ApplicationId}",
                notification.EventId, notification.ApplicationId);
        }
    }
}
