using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.DeletePermission;

/// <summary>
/// Handler for deleting a permission.
/// </summary>
public class DeletePermissionCommandHandler : IRequestHandler<DeletePermissionCommand, ErrorOr<bool>>
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILogger<DeletePermissionCommandHandler> _logger;

    public DeletePermissionCommandHandler(
        IPermissionRepository permissionRepository,
        ILogger<DeletePermissionCommandHandler> logger)
    {
        _permissionRepository = permissionRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<bool>> Handle(DeletePermissionCommand request, CancellationToken cancellationToken)
    {
        var permission = await _permissionRepository.GetByIdAsync(request.Id, cancellationToken);

        if (permission == null)
        {
            return PermissionErrors.NotFound(request.Id);
        }

        // Check if permission is a system/core permission (e.g., global wildcard)
        if (permission.Code == "*")
        {
            return PermissionErrors.CannotDeleteSystemPermission;
        }

        await _permissionRepository.DeleteAsync(request.Id, cancellationToken);

        _logger.LogInformation(
            "Permission deleted: {PermissionId} ({PermissionCode}) by {DeletedBy}",
            request.Id, permission.Code, request.DeletedBy);

        return true;
    }
}
