using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when organization ownership has been transferred.
/// </summary>
public class OrganizationOwnershipTransferredAuditEventHandler
    : INotificationHandler<OrganizationOwnershipTransferredEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<OrganizationOwnershipTransferredAuditEventHandler> _logger;

    public OrganizationOwnershipTransferredAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<OrganizationOwnershipTransferredAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(OrganizationOwnershipTransferredEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.OrganizationManagement,
            action: AuditActions.OrganizationOwnershipTransferred,
            userId: notification.NewOwnerId,
            performedBy: notification.TransferredBy,
            entityType: "Organization",
            entityId: notification.OrganizationId,
            oldValues: $"{{\"ownerId\":\"{notification.PreviousOwnerId}\"}}",
            newValues: $"{{\"ownerId\":\"{notification.NewOwnerId}\"}}",
            additionalData: $"{{\"viaPlatformScope\":{(notification.ViaPlatformScope ? "true" : "false")}}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug(
            "Audit log created for OrganizationOwnershipTransferredEvent: {OrganizationId}",
            notification.OrganizationId);
    }
}
