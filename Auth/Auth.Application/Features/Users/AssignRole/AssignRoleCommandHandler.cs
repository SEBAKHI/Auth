using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
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
    private readonly IPublisher _publisher;
    private readonly ILogger<AssignRoleCommandHandler> _logger;

    public AssignRoleCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPublisher publisher,
        ILogger<AssignRoleCommandHandler> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
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

        // Check if already assigned
        var existingRoles = await _roleRepository.GetUserRolesAsync(request.UserId, cancellationToken);
        if (existingRoles.Any(r => r.Id == request.RoleId))
        {
            return Error.Conflict(
                code: "User.RoleAlreadyAssigned",
                description: $"User already has role '{role.Name}'.");
        }

        // Create the assignment
        var userRole = UserRole.Create(
            userId: request.UserId,
            roleId: request.RoleId,
            assignedBy: request.AssignedBy,
            applicationId: null,
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
