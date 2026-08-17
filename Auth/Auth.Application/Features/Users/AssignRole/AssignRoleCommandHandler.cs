using Auth.Application.Common;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;

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
    private readonly IPublisher _publisher;
    private readonly ILogger<AssignRoleCommandHandler> _logger;

    public AssignRoleCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IApplicationRepository applicationRepository,
        IPermissionRepository permissionRepository,
        PermissionGrantGuard grantGuard,
        IPublisher publisher,
        ILogger<AssignRoleCommandHandler> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _applicationRepository = applicationRepository;
        _permissionRepository = permissionRepository;
        _grantGuard = grantGuard;
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
        if (request.ApplicationId.HasValue)
        {
            var application = await _applicationRepository.GetByIdAsync(request.ApplicationId.Value, cancellationToken);
            if (application is null)
            {
                return ApplicationErrors.NotFound(request.ApplicationId.Value);
            }

            if (role.ApplicationId.HasValue && role.ApplicationId.Value != request.ApplicationId.Value)
            {
                return RoleErrors.NotFound(request.RoleId);
            }
        }

        // No amplification. Assigning a role hands over every permission that
        // role carries, so it is a grant by another name — and the one with the
        // widest blast radius, because super-admin is just a role and nothing
        // else stopped a users:manage-roles holder from assigning it to itself.
        // Checked against the role's CURRENT permissions: a role that gains one
        // later is re-evaluated on the next assignment, and existing assignments
        // are a separate concern from who may create new ones.
        var rolePermissions = await _permissionRepository.GetRolePermissionsAsync(
            request.RoleId, cancellationToken);

        var canGrant = await _grantGuard.EnsureCanGrantAsync(
            request.AssignedBy,
            rolePermissions.Select(p => p.Code.Value),
            cancellationToken);
        if (canGrant.IsError)
        {
            _logger.LogWarning(
                "Blocked assignment of role {RoleId} ({RoleName}) to user {UserId}: actor {AssignedBy} does not hold every permission the role carries",
                request.RoleId, role.Name, request.UserId, request.AssignedBy);
            return canGrant.Errors;
        }

        // Scoped by (role, application): a platform-wide assignment of the same
        // role must not block scoping it to one application, and vice versa.
        // Comparing role alone conflated the two and made the second impossible.
        var existing = await _userRepository.GetUserRoleAsync(
            request.UserId, request.RoleId, request.ApplicationId, cancellationToken);

        if (existing is not null && existing.IsValid())
        {
            return Error.Conflict(
                code: "User.RoleAlreadyAssigned",
                description: $"User already has role '{role.Name}'.",
                metadata: new() { ["args"] = new object[] { role.Name } });
        }

        // Create the assignment
        var userRole = UserRole.Create(
            userId: request.UserId,
            roleId: request.RoleId,
            assignedBy: request.AssignedBy,
            applicationId: request.ApplicationId,
            expiresAt: request.ExpiresAt);

        await _roleRepository.AssignToUserAsync(userRole, cancellationToken);

        _logger.LogInformation(
            "Role {RoleId} ({RoleName}) assigned to user {UserId} by {AssignedBy}",
            request.RoleId, role.Name, request.UserId, request.AssignedBy);

        await _publisher.Publish(
            new RoleAssignedEvent(request.UserId, request.RoleId, role.Name, request.AssignedBy),
            cancellationToken);

        return Result.Success;
    }
}
