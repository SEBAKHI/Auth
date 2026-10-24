using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an application role is assigned to a member within an
/// organization.
/// </summary>
public class OrganizationAppRoleAssignedAuditEventHandler : INotificationHandler<OrganizationAppRoleAssignedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<OrganizationAppRoleAssignedAuditEventHandler> _logger;

    public OrganizationAppRoleAssignedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<OrganizationAppRoleAssignedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(OrganizationAppRoleAssignedEvent notification, CancellationToken cancellationToken)
    {
        // Serialized, never interpolated: a role name is free text.
        var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
            actionType: AuditActionTypes.Authorization,
            action: AuditActions.OrganizationAppRoleAssigned,
            userId: notification.UserId,
            performedBy: notification.AssignedBy,
            applicationId: notification.ApplicationId,
            entityType: "OrganizationUserRole",
            entityId: notification.UserId,
            additionalData: JsonSerializer.Serialize(new
            {
                organizationId = notification.OrganizationId,
                roleId = notification.RoleId,
                roleName = notification.RoleName, expiresAt = notification.ExpiresAt
            }));

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug(
            "Audit log created for OrganizationAppRoleAssignedEvent: User {UserId}, Role {RoleId}, Organization {OrganizationId}",
            notification.UserId, notification.RoleId, notification.OrganizationId);
    }
}
