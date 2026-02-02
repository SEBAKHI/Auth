using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.UserManagement.Commands;

/// <summary>
/// Handler for removing a role from a user.
/// </summary>
public class RemoveUserRoleCommandHandler : IRequestHandler<RemoveUserRoleCommand, ErrorOr<bool>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<RemoveUserRoleCommandHandler> _logger;

    public RemoveUserRoleCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ILogger<RemoveUserRoleCommandHandler> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<bool>> Handle(RemoveUserRoleCommand request, CancellationToken cancellationToken)
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

        // Check if user has this role
        if (!await _userRepository.HasRoleAsync(request.UserId, request.RoleId, cancellationToken))
        {
            return RoleErrors.RoleNotAssigned(request.UserId, request.RoleId);
        }

        await _userRepository.RemoveRoleAsync(request.UserId, request.RoleId, request.ApplicationId, cancellationToken);

        _logger.LogInformation(
            "Role removed from user: User {UserId}, Role {RoleId} ({RoleCode}) by {RemovedBy}",
            request.UserId, request.RoleId, role.Code, request.RemovedBy);

        return true;
    }
}
