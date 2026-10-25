using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a member is removed from an organization: one row for
/// the removal, listing the application roles and permissions it deleted with the
/// membership. Written best-effort like the other audit handlers here: the removal has
/// already committed, so a failure is logged, never propagated.
/// </summary>
public class OrganizationMemberRemovedAuditEventHandler : INotificationHandler<OrganizationMemberRemovedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<OrganizationMemberRemovedAuditEventHandler> _logger;

    public OrganizationMemberRemovedAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<OrganizationMemberRemovedAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(OrganizationMemberRemovedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // Serialized, never interpolated: role names are free text.
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Authorization,
                action: AuditActions.OrganizationMemberRemoved,
                userId: notification.UserId,
                performedBy: notification.RemovedBy,
                entityType: "OrganizationUser",
                entityId: notification.UserId,
                additionalData: JsonSerializer.Serialize(new
                {
                    organizationId = notification.OrganizationId,
                    removedApplicationRoles = notification.RemovedApplicationRoles.Select(role => new
                    {
                        applicationId = role.ApplicationId,
                        roleId = role.RoleId,
                        roleCode = role.RoleCode,
                        roleName = role.RoleName
                    }),
                    removedPermissions = notification.RemovedPermissions.Select(permission => new
                    {
                        applicationId = permission.ApplicationId,
                        permissionId = permission.PermissionId,
                        permissionCode = permission.PermissionCode
                    })
                }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug(
                "Audit log created for OrganizationMemberRemovedEvent: User {UserId}, Organization {OrganizationId}",
                notification.UserId, notification.OrganizationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for OrganizationMemberRemovedEvent: User {UserId}, Organization {OrganizationId}, RemovedBy {RemovedBy}",
                notification.UserId, notification.OrganizationId, notification.RemovedBy);
        }
    }
}
