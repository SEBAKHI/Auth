using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an application's launch addresses changed (OI-192): the
/// initiate-login URI and the base URL, where an accepted invitation sends the invitee, each with
/// what it held before and after.
/// </summary>
/// <remarks>
/// Best-effort, like its sibling for the redirect allowlists
/// (<see cref="ApplicationRedirectUrisChangedAuditEventHandler"/>): the save has already
/// committed, so a failure here is logged and never propagated. The details are serialized, not
/// interpolated: the addresses are an administrator's free text.
/// </remarks>
public class ApplicationLaunchAddressesChangedAuditEventHandler : INotificationHandler<ApplicationLaunchAddressesChangedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<ApplicationLaunchAddressesChangedAuditEventHandler> _logger;

    public ApplicationLaunchAddressesChangedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<ApplicationLaunchAddressesChangedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(ApplicationLaunchAddressesChangedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Application,
                action: AuditActions.ApplicationLaunchAddressesChanged,
                performedBy: notification.TriggeredBy,
                applicationId: notification.ApplicationId,
                entityType: "Application",
                entityId: notification.ApplicationId,
                additionalData: JsonSerializer.Serialize(new
                {
                    applicationCode = notification.ApplicationCode,
                    initiateLoginUri = notification.InitiateLoginUri,
                    baseUrl = notification.BaseUrl
                }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for ApplicationLaunchAddressesChangedEvent {EventId}: application {ApplicationId}",
                notification.EventId, notification.ApplicationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for ApplicationLaunchAddressesChangedEvent {EventId}: application {ApplicationId}",
                notification.EventId, notification.ApplicationId);
        }
    }
}
