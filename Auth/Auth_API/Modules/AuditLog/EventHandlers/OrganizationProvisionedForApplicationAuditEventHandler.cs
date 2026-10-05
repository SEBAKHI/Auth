using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an application's organization-creation step
/// set up an organization: enabled the application there and granted its
/// creator role to the user.
/// </summary>
/// <remarks>
/// The row records that the role grant did not pass the organization grant
/// guard: its authority is the platform administrator's application setting,
/// checked when that setting was saved, not the user who triggered it.
/// </remarks>
public class OrganizationProvisionedForApplicationAuditEventHandler
    : INotificationHandler<OrganizationProvisionedForApplicationEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<OrganizationProvisionedForApplicationAuditEventHandler> _logger;

    public OrganizationProvisionedForApplicationAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<OrganizationProvisionedForApplicationAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(OrganizationProvisionedForApplicationEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.OrganizationManagement,
            action: AuditActions.OrganizationProvisionedForApplication,
            userId: notification.UserId,
            performedBy: notification.UserId,
            applicationId: notification.ApplicationId,
            entityType: "Organization",
            entityId: notification.OrganizationId,
            newValues: $"{{\"applicationId\":\"{notification.ApplicationId}\",\"roleId\":\"{notification.CreatorRoleId}\"}}",
            additionalData:
                $"{{\"organizationCreated\":{(notification.OrganizationCreated ? "true" : "false")}," +
                "\"grantAuthority\":\"application-creator-role-setting\"," +
                $"\"occurredAtUtc\":\"{notification.OccurredAtUtc:O}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug(
            "Audit log created for OrganizationProvisionedForApplicationEvent: {OrganizationId}",
            notification.OrganizationId);
    }
}
