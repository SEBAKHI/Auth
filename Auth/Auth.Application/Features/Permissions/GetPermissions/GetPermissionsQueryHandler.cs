using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.GetPermissions;

/// <summary>
/// Handler for getting permissions.
/// </summary>
public class GetPermissionsQueryHandler : IRequestHandler<GetPermissionsQuery, ErrorOr<IReadOnlyList<PermissionDto>>>
{
    private readonly IPermissionRepository _permissionRepository;

    public GetPermissionsQueryHandler(IPermissionRepository permissionRepository)
    {
        _permissionRepository = permissionRepository;
    }

    public async Task<ErrorOr<IReadOnlyList<PermissionDto>>> Handle(
        GetPermissionsQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Permission> permissions;

        if (request.ApplicationId.HasValue)
        {
            permissions = await _permissionRepository.GetByApplicationAsync(
                request.ApplicationId.Value, request.SortBy, request.SortDirection, cancellationToken);
        }
        else
        {
            permissions = await _permissionRepository.GetAllAsync(
                request.SortBy, request.SortDirection, cancellationToken);
        }

        var permissionDtos = permissions.Select(p => new PermissionDto
        {
            Id = p.Id,
            ApplicationId = p.ApplicationId,
            Code = p.Code,
            Name = p.Name,
            Description = p.Description,
            ParentId = p.ParentId,
            Level = p.Level,
            IsWildcard = p.IsWildcard,
            IsActive = p.IsActive,
            CreatedAt = p.CreatedAt,
            ModifiedAt = p.ModifiedAt
        }).ToList();

        return permissionDtos;
    }
}
