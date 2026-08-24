using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.DeleteRole;

/// <summary>
/// Handler for deleting a role.
/// </summary>
public class DeleteRoleCommandHandler : IRequestHandler<DeleteRoleCommand, ErrorOr<Success>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPublisher _publisher;
    private readonly ILogger<DeleteRoleCommandHandler> _logger;

    public DeleteRoleCommandHandler(
        IRoleRepository roleRepository,
        IPublisher publisher,
        ILogger<DeleteRoleCommandHandler> logger)
    {
        _roleRepository = roleRepository;
        _publisher = publisher;
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

        await _publisher.Publish(
            new RoleDeletedEvent(role.Id, role.Code, role.Name, request.DeletedBy),
            cancellationToken);

        return Result.Success;
    }
}
