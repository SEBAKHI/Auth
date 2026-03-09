using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Application.DTOs;
using Auth_Lib.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Permissions.GetPermissionById;

/// <summary>
/// Handler for getting a permission by ID.
/// </summary>
public class GetPermissionByIdQueryHandler : IRequestHandler<GetPermissionByIdQuery, ErrorOr<PermissionDto>>
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILogger<GetPermissionByIdQueryHandler> _logger;

    public GetPermissionByIdQueryHandler(
        IPermissionRepository permissionRepository,
        ILogger<GetPermissionByIdQueryHandler> logger)
    {
        _permissionRepository = permissionRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<PermissionDto>> Handle(GetPermissionByIdQuery request, CancellationToken cancellationToken)
    {
        var permission = await _permissionRepository.GetByIdAsync(request.Id, cancellationToken);

        if (permission == null)
        {
            return PermissionErrors.NotFound(request.Id);
        }

        _logger.LogDebug("Retrieved permission {PermissionId} ({PermissionCode})", permission.Id, permission.Code);

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
