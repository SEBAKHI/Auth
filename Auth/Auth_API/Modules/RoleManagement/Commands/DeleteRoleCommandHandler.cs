using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.RoleManagement.Commands;

/// <summary>
/// Handler for deleting a role.
/// </summary>
public class DeleteRoleCommandHandler : IRequestHandler<DeleteRoleCommand, ErrorOr<Success>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<DeleteRoleCommandHandler> _logger;

    public DeleteRoleCommandHandler(
        IRoleRepository roleRepository,
        ILogger<DeleteRoleCommandHandler> logger)
    {
        _roleRepository = roleRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(request.Id, cancellationToken);
        if (role == null)
        {
            return RoleErrors.NotFound(request.Id);
        }

        // Cannot delete system roles
        if (role.IsSystem)
        {
            return Error.Forbidden(
                code: "Role.CannotDeleteSystemRole",
                description: "System roles cannot be deleted.");
        }

        await _roleRepository.DeleteAsync(request.Id, cancellationToken);

        _logger.LogInformation(
            "Role deleted: {RoleId} by {DeletedBy}",
            request.Id, request.DeletedBy);

        return Result.Success;
    }
}
