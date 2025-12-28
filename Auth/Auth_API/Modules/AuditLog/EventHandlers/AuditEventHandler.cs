using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Events;
using Auth_Lib.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Handles all domain events and creates audit log entries.
/// </summary>
public class AuditEventHandler :
    INotificationHandler<UserCreatedEvent>,
    INotificationHandler<UserLoggedInEvent>,
    INotificationHandler<UserLoggedOutEvent>,
    INotificationHandler<PasswordChangedEvent>,
    INotificationHandler<RoleAssignedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<AuditEventHandler> _logger;

    public AuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<AuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserCreatedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth_Lib.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "UserManagement",
            action: "user.created",
            userId: notification.CreatedBy,
            entityType: "User",
            entityId: notification.UserId,
            additionalData: $"{{\"email\":\"{notification.Email}\",\"firstName\":\"{notification.FirstName}\",\"lastName\":\"{notification.LastName}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for UserCreatedEvent: {UserId}", notification.UserId);
    }

    public async Task Handle(UserLoggedInEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth_Lib.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Authentication",
            action: "user.login",
            userId: notification.UserId,
            entityType: "User",
            entityId: notification.UserId,
            ipAddress: notification.IpAddress,
            userAgent: notification.UserAgent);

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for UserLoggedInEvent: {UserId}", notification.UserId);
    }

    public async Task Handle(UserLoggedOutEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth_Lib.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Authentication",
            action: notification.AllDevices ? "user.logout.all" : "user.logout",
            userId: notification.UserId,
            entityType: "User",
            entityId: notification.UserId);

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for UserLoggedOutEvent: {UserId}", notification.UserId);
    }

    public async Task Handle(PasswordChangedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth_Lib.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Security",
            action: "password.changed",
            userId: notification.ChangedBy,
            entityType: "User",
            entityId: notification.UserId);

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for PasswordChangedEvent: {UserId}", notification.UserId);
    }

    public async Task Handle(RoleAssignedEvent notification, CancellationToken cancellationToken)
    {
        var log = Auth_Lib.Domain.Entities.AuditLog.CreateSuccess(
            actionType: "Authorization",
            action: "role.assigned",
            userId: notification.AssignedBy,
            entityType: "UserRole",
            entityId: notification.UserId,
            additionalData: $"{{\"roleId\":\"{notification.RoleId}\",\"roleName\":\"{notification.RoleName}\"}}");

        await _auditLogRepository.CreateAsync(log, cancellationToken);
        _logger.LogDebug("Audit log created for RoleAssignedEvent: User {UserId}, Role {RoleId}",
            notification.UserId, notification.RoleId);
    }
}
