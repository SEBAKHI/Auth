using Auth.Application.Common;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth.Application.Features.Users.AssignRole;

/// <summary>
/// Handler for assigning a role to a user.
/// </summary>
public class AssignRoleCommandHandler : IRequestHandler<AssignRoleCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly PermissionGrantGuard _grantGuard;
    private readonly PlatformGrantFactorGuard _factorGuard;
    private readonly ApplicationAccessWriter _accessWriter;
    private readonly IPublisher _publisher;
    private readonly ILogger<AssignRoleCommandHandler> _logger;

    public AssignRoleCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IApplicationRepository applicationRepository,
        IPermissionRepository permissionRepository,
        PermissionGrantGuard grantGuard,
        PlatformGrantFactorGuard factorGuard,
        ApplicationAccessWriter accessWriter,
        IPublisher publisher,
        ILogger<AssignRoleCommandHandler> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _applicationRepository = applicationRepository;
        _permissionRepository = permissionRepository;
        _grantGuard = grantGuard;
        _factorGuard = factorGuard;
        _accessWriter = accessWriter;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(AssignRoleCommand request, CancellationToken cancellationToken)
    {
        // Verify user exists
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // Verify role exists
        var role = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (role == null)
        {
            return RoleErrors.NotFound(request.RoleId);
        }

        // A role that belongs to one application cannot be scoped to another.
        // Global roles (null owner) may be scoped anywhere.
        ApplicationEntity? application = null;
        if (request.ApplicationId.HasValue)
        {
            application = await _applicationRepository.GetByIdAsync(request.ApplicationId.Value, cancellationToken);
            if (application is null)
            {
                return ApplicationErrors.NotFound(request.ApplicationId.Value);
            }

            if (role.ApplicationId.HasValue && role.ApplicationId.Value != request.ApplicationId.Value)
            {
                return RoleErrors.NotFound(request.RoleId);
            }
        }

        // A role owned by an application is assigned at that application's scope,
        // whatever the request carries: the user page sends none, and a row without
        // scope looked like a platform role, came without access to the
        // application, and never asked for applications:update.
        var scope = role.ApplicationId ?? request.ApplicationId;
        if (application is null && scope is { } ownerId)
        {
            application = await _applicationRepository.GetByIdAsync(ownerId, cancellationToken);
            if (application is null)
            {
                return ApplicationErrors.NotFound(ownerId);
            }
        }

        // No amplification. Assigning a role hands over every permission that
        // role carries, so it is a grant by another name — and the one with the
        // widest blast radius, because super-admin is just a role and nothing
        // else stopped a users:manage-roles holder from assigning it to itself.
        // Checked against the role's CURRENT permissions: a role that gains one
        // later is re-evaluated on the next assignment, and existing assignments
        // are a separate concern from who may create new ones. Authority inside an
        // application is what the application page gates with applications:update,
        // so an assignment at an application's scope asks for it too, in the same read.
        var rolePermissions = await _permissionRepository.GetRolePermissionsAsync(
            request.RoleId, cancellationToken);

        var requestedCodes = rolePermissions.Select(p => p.Code.Value);
        if (application is not null)
        {
            requestedCodes = requestedCodes.Append(PermissionCodes.Applications.Update);
        }

        var canGrant = await _grantGuard.EnsureCanGrantAsync(
            request.AssignedBy,
            requestedCodes,
            cancellationToken);
        if (canGrant.IsError)
        {
            _logger.LogWarning(
                "Blocked assignment of role {RoleId} ({RoleName}) to user {UserId} at application {ApplicationId}: actor {AssignedBy} does not hold every permission it requires",
                request.RoleId, role.Name, request.UserId, scope, request.AssignedBy);
            return canGrant.Errors;
        }

        // Scoped by (role, application): a platform-wide assignment of the same
        // role must not block scoping it to one application, and vice versa.
        // Comparing role alone conflated the two and made the second impossible.
        var existing = await _userRepository.GetUserRoleAsync(
            request.UserId, request.RoleId, scope, cancellationToken);

        if (existing is not null && existing.IsValid())
        {
            return UserErrors.RoleAlreadyAssigned(role.Name);
        }

        // While platform administrators must use two-step verification, a platform
        // role goes only to an account that already has its factor: otherwise
        // whoever sets one up first would own the role's authority.
        var factorReady = await _factorGuard.EnsureAccountMayReceiveAsync(
            request.UserId,
            atPlatformScope: scope is null,
            rolePermissions,
            cancellationToken);
        if (factorReady.IsError)
        {
            return factorReady.Errors;
        }

        // Every check is done. A role inside an application carries access to it,
        // or a Restricted application never lets the user sign in to use it.
        if (application is not null)
        {
            await _accessWriter.EnsureAsync(
                application, request.UserId, request.AssignedBy, request.ExpiresAt, cancellationToken);
        }

        // Create the assignment
        var userRole = UserRole.Create(
            userId: request.UserId,
            roleId: request.RoleId,
            assignedBy: request.AssignedBy,
            applicationId: scope,
            expiresAt: request.ExpiresAt);

        await _roleRepository.AssignToUserAsync(userRole, cancellationToken);

        _logger.LogInformation(
            "Role {RoleId} ({RoleName}) assigned to user {UserId} at application {ApplicationId} by {AssignedBy}",
            request.RoleId, role.Name, request.UserId, scope, request.AssignedBy);

        await _publisher.Publish(
            new RoleAssignedEvent(request.UserId, request.RoleId, role.Name, request.AssignedBy, scope),
            cancellationToken);

        return Result.Success;
    }
}
