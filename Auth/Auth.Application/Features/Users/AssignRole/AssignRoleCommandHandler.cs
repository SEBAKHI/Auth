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
    private readonly IPublisher _publisher;
    private readonly ILogger<AssignRoleCommandHandler> _logger;

    public AssignRoleCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IApplicationRepository applicationRepository,
        IPublisher publisher,
        ILogger<AssignRoleCommandHandler> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _applicationRepository = applicationRepository;
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
