using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an organization ownership transfer is
/// initiated (confirmation code sent to the prospective new owner).
/// </summary>
public class OrganizationOwnershipTransferInitiatedAuditEventHandler
    : INotificationHandler<OrganizationOwnershipTransferInitiatedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<OrganizationOwnershipTransferInitiatedAuditEventHandler> _logger;

    public OrganizationOwnershipTransferInitiatedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<OrganizationOwnershipTransferInitiatedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(OrganizationOwnershipTransferInitiatedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "OrganizationManagement",
            action: "organization.ownership_transfer_initiated",
            userId: notification.TargetUserId,
            performedBy: notification.InitiatedBy,
            entityType: "Organization",
            entityId: notification.OrganizationId,
            additionalData: $"{{\"targetUserId\":\"{notification.TargetUserId}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug(
            "Audit log created for OrganizationOwnershipTransferInitiatedEvent: {OrganizationId}",
            notification.OrganizationId);
    }
}
