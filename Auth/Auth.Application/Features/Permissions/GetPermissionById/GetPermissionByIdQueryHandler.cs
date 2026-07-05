using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.GetPermissionById;

/// <summary>
/// Handler for getting a permission by ID.
/// </summary>
public class GetPermissionByIdQueryHandler : IRequestHandler<GetPermissionByIdQuery, ErrorOr<PermissionDto>>
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<GetPermissionByIdQueryHandler> _logger;

    public GetPermissionByIdQueryHandler(
        IPermissionRepository permissionRepository,
        IApplicationRepository applicationRepository,
        ILogger<GetPermissionByIdQueryHandler> logger)
    {
        _permissionRepository = permissionRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<PermissionDto>> Handle(GetPermissionByIdQuery request, CancellationToken cancellationToken)
    {
        var permission = await _permissionRepository.GetByIdAsync(request.Id, cancellationToken);

        if (permission == null)
        {
            return PermissionErrors.NotFound(request.Id);
        }

        var applicationNames = await NameLookupHelper.ApplicationNamesAsync(
            _applicationRepository, [permission.ApplicationId], cancellationToken);

        _logger.LogDebug("Retrieved permission {PermissionId} ({PermissionCode})", permission.Id, permission.Code);

        return new PermissionDto
        {
            Id = permission.Id,
            ApplicationId = permission.ApplicationId,
            ApplicationName = permission.ApplicationId.HasValue
                ? applicationNames.GetValueOrDefault(permission.ApplicationId.Value)
                : null,
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
