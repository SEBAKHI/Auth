using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when an application role is removed from a member within an
/// organization. Written best-effort like the other audit handlers here: the removal has
/// already committed, so a failure is logged, never propagated.
/// </summary>
public class OrganizationAppRoleRemovedAuditEventHandler : INotificationHandler<OrganizationAppRoleRemovedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<OrganizationAppRoleRemovedAuditEventHandler> _logger;

    public OrganizationAppRoleRemovedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<OrganizationAppRoleRemovedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(OrganizationAppRoleRemovedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // Serialized, never interpolated: a role name is free text.
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Authorization,
                action: AuditActions.OrganizationAppRoleRemoved,
                userId: notification.UserId,
                performedBy: notification.RemovedBy,
                applicationId: notification.ApplicationId,
                entityType: "OrganizationUserRole",
                entityId: notification.UserId,
                additionalData: JsonSerializer.Serialize(new
                {
                    organizationId = notification.OrganizationId,
                    roleId = notification.RoleId,
                    roleName = notification.RoleName
                }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for OrganizationAppRoleRemovedEvent: User {UserId}, Role {RoleId}, Organization {OrganizationId}",
                notification.UserId, notification.RoleId, notification.OrganizationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for OrganizationAppRoleRemovedEvent: User {UserId}, Role {RoleId}, Application {ApplicationId}, Organization {OrganizationId}",
                notification.UserId, notification.RoleId, notification.ApplicationId, notification.OrganizationId);
        }
    }
}
