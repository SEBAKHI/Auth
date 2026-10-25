using Auth.Application.Common;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GrantApplicationAccess;

/// <summary>
/// Handler for inviting a user to an application.
/// </summary>
public class GrantApplicationAccessCommandHandler : IRequestHandler<GrantApplicationAccessCommand, ErrorOr<Success>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly ApplicationAccessWriter _accessWriter;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly PermissionGrantGuard _grantGuard;
    private readonly IPublisher _publisher;
    private readonly ILogger<GrantApplicationAccessCommandHandler> _logger;

    public GrantApplicationAccessCommandHandler(
        IApplicationRepository applicationRepository,
        ApplicationAccessWriter accessWriter,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        PermissionGrantGuard grantGuard,
        IPublisher publisher,
        ILogger<GrantApplicationAccessCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _accessWriter = accessWriter;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _grantGuard = grantGuard;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        GrantApplicationAccessCommand request,
        CancellationToken cancellationToken)
    {
        var application = await _applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        Role? role = null;
        if (request.RoleId.HasValue)
        {
            role = await _roleRepository.GetByIdAsync(request.RoleId.Value, cancellationToken);
            if (role is null)
            {
                return RoleErrors.NotFound(request.RoleId.Value);
            }

            // A role owned by another application cannot be granted here.
            // Platform-wide roles (null owner) may be scoped to any application.
            if (role.ApplicationId.HasValue && role.ApplicationId.Value != request.ApplicationId)
            {
                return RoleErrors.NotFound(request.RoleId.Value);
            }

            // No amplification, before anything is written: the role's permissions
            // travel into this application's tokens, so handing the role over is
            // granting them, as in AssignRoleCommandHandler. Checked against the
            // role's CURRENT permissions and the granter's live platform permissions;
            // a refusal writes neither the access grant nor the role.
            var rolePermissions = await _permissionRepository.GetRolePermissionsAsync(role.Id, cancellationToken);
            var canGrant = await _grantGuard.EnsureCanGrantAsync(
                request.GrantedBy,
                rolePermissions.Select(p => p.Code.Value),
                cancellationToken);
            if (canGrant.IsError)
            {
                _logger.LogWarning(
                    "Blocked granting application {ApplicationId} access with role {RoleId} to user {UserId}: actor {GrantedBy} does not hold every permission the role carries",
                    request.ApplicationId, role.Id, request.UserId, request.GrantedBy);
                return canGrant.Errors;
            }
        }

        // Re-inviting someone whose invitation is still valid is this command's
        // conflict. A revoked or lapsed one is reinstated on its own row by the
        // shared writer (ApplicationAccessWriter).
        var existing = await _accessWriter.GetAsync(
            request.ApplicationId, request.UserId, cancellationToken);

        if (existing is not null && existing.IsValid())
        {
            return ApplicationErrors.UserAccessAlreadyGranted(request.UserId);
        }

        await _accessWriter.WriteAsync(
            application, request.UserId, existing, request.GrantedBy, request.ExpiresAt, request.Note, cancellationToken);

        if (role is not null)
        {
            // Scoped to this application, so the role travels only in tokens
            // minted for it. A duplicate is not an error here: the invitation is
            // the point of this command, and re-inviting someone who kept their
            // role should not fail.
            var existingRole = await _userRepository.GetUserRoleAsync(
                request.UserId, role.Id, request.ApplicationId, cancellationToken);

            if (existingRole is null || !existingRole.IsValid())
            {
                var userRole = UserRole.Create(
                    userId: request.UserId,
                    roleId: role.Id,
                    assignedBy: request.GrantedBy,
                    applicationId: request.ApplicationId,
                    expiresAt: request.ExpiresAt);

                await _roleRepository.AssignToUserAsync(userRole, cancellationToken);

                await _publisher.Publish(
                    new RoleAssignedEvent(request.UserId, role.Id, role.Name, request.GrantedBy, request.ApplicationId),
                    cancellationToken);
            }
        }

        _logger.LogInformation(
            "User {UserId} granted access to application {ApplicationId} ({ApplicationCode}) by {GrantedBy}",
            request.UserId, application.Id, application.Code, request.GrantedBy);

        await _accessWriter.PublishGrantedAsync(
            application, request.UserId, request.ExpiresAt, request.GrantedBy, cancellationToken);

        return Result.Success;
    }
}
