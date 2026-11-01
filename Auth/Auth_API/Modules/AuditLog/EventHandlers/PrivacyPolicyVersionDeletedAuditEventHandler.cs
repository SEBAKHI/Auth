using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a draft privacy-policy revision was deleted (only a draft that
/// was never published, announced or cited can be). The row keeps the version identifier.
/// </summary>
/// <remarks>
/// Best-effort, like its sibling for deleted sign-in page designs: the change has already
/// committed, so a failure here is logged and never propagated.
/// </remarks>
public class PrivacyPolicyVersionDeletedAuditEventHandler : INotificationHandler<PrivacyPolicyVersionDeletedEvent>
{
    private static readonly JsonSerializerOptions Camel = new(JsonSerializerDefaults.Web);

    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<PrivacyPolicyVersionDeletedAuditEventHandler> _logger;

    public PrivacyPolicyVersionDeletedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<PrivacyPolicyVersionDeletedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(PrivacyPolicyVersionDeletedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Administration,
                action: AuditActions.PrivacyPolicyVersionDeleted,
                performedBy: notification.TriggeredBy,
                applicationId: null,
                entityType: "PrivacyPolicyVersion",
                entityId: notification.PrivacyPolicyVersionId,
                oldValues: JsonSerializer.Serialize(new { notification.Version }, Camel),
                newValues: null,
                additionalData: null);

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for PrivacyPolicyVersionDeletedEvent {EventId}: version {VersionId}",
                notification.EventId, notification.PrivacyPolicyVersionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for PrivacyPolicyVersionDeletedEvent {EventId}: version {VersionId}",
                notification.EventId, notification.PrivacyPolicyVersionId);
        }
    }
}
