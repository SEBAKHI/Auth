using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
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
    private readonly IApplicationRepository _applicationRepository;
    private readonly IUserRepository _userRepository;

    public GetPermissionsQueryHandler(
        IPermissionRepository permissionRepository,
        IApplicationRepository applicationRepository,
        IUserRepository userRepository)
    {
        _permissionRepository = permissionRepository;
        _applicationRepository = applicationRepository;
        _userRepository = userRepository;
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

        var applicationNames = await NameLookupHelper.ApplicationNamesAsync(
            _applicationRepository,
            permissions.Select(p => p.ApplicationId),
            cancellationToken);

        var userNames = await NameLookupHelper.UserNamesAsync(
            _userRepository,
            permissions.SelectMany(p => new Guid?[] { p.CreatedBy, p.ModifiedBy }),
            cancellationToken);

        var permissionDtos = permissions.Select(p => new PermissionDto
        {
            Id = p.Id,
            ApplicationId = p.ApplicationId,
            ApplicationName = p.ApplicationId.HasValue
                ? applicationNames.GetValueOrDefault(p.ApplicationId.Value)
                : null,
            Code = p.Code,
            Name = p.Name,
            Description = p.Description,
            ParentId = p.ParentId,
            Level = p.Level,
            IsWildcard = p.IsWildcard,
            IsActive = p.IsActive,
            CreatedAt = p.CreatedAt,
            CreatedBy = p.CreatedBy,
            CreatedByName = userNames.GetValueOrDefault(p.CreatedBy),
            ModifiedAt = p.ModifiedAt,
            ModifiedBy = p.ModifiedBy,
            ModifiedByName = p.ModifiedBy.HasValue
                ? userNames.GetValueOrDefault(p.ModifiedBy.Value)
                : null
        }).ToList();

        return permissionDtos;
    }
}
