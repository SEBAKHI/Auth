using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.UpdatePermission;

/// <summary>
/// Handler for updating an existing permission.
/// </summary>
public class UpdatePermissionCommandHandler : IRequestHandler<UpdatePermissionCommand, ErrorOr<PermissionDto>>
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILogger<UpdatePermissionCommandHandler> _logger;

    public UpdatePermissionCommandHandler(
        IPermissionRepository permissionRepository,
        ILogger<UpdatePermissionCommandHandler> logger)
    {
        _permissionRepository = permissionRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<PermissionDto>> Handle(UpdatePermissionCommand request, CancellationToken cancellationToken)
    {
        var permission = await _permissionRepository.GetByIdAsync(request.Id, cancellationToken);

        if (permission == null)
        {
            return PermissionErrors.NotFound(request.Id);
        }

        // Update permission (only name and description can be updated, not the code)
        permission.Update(request.Name, request.Description, request.ModifiedBy);

        await _permissionRepository.UpdateAsync(permission, cancellationToken);

        _logger.LogInformation(
            "Permission updated: {PermissionId} ({PermissionCode}) by {ModifiedBy}",
            permission.Id, permission.Code, request.ModifiedBy);

        return new PermissionDto
        {
            Id = permission.Id,
            ApplicationId = permission.ApplicationId,
            Code = permission.Code,
            Name = permission.Name,
            Description = permission.Description,
            ParentId = permission.ParentId,
            Level = permission.Level,
            IsWildcard = permission.IsWildcard,
            IsActive = permission.IsActive,
            CreatedAt = permission.CreatedAt,
            ModifiedAt = permission.ModifiedAt
        };
    }
}
