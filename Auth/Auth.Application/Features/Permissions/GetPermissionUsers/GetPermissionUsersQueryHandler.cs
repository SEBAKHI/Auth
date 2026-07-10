using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.GetPermissionUsers;

/// <summary>
/// Handler for getting paginated users granted a permission.
/// </summary>
public class GetPermissionUsersQueryHandler : IRequestHandler<GetPermissionUsersQuery, ErrorOr<PagedPermissionUsersDto>>
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly ILogger<GetPermissionUsersQueryHandler> _logger;

    public GetPermissionUsersQueryHandler(
        IPermissionRepository permissionRepository,
        IImageUrlComposer imageUrlComposer,
        ILogger<GetPermissionUsersQueryHandler> logger)
    {
        _permissionRepository = permissionRepository;
        _imageUrlComposer = imageUrlComposer;
        _logger = logger;
    }

    public async Task<ErrorOr<PagedPermissionUsersDto>> Handle(
        GetPermissionUsersQuery request,
        CancellationToken cancellationToken)
    {
        // Verify permission exists
        var permission = await _permissionRepository.GetByIdAsync(request.PermissionId, cancellationToken);
        if (permission == null)
        {
            return PermissionErrors.NotFound(request.PermissionId);
        }

        var (users, totalCount) = await _permissionRepository.GetUsersPagedAsync(
            request.PermissionId,
            request.PageNumber,
            request.PageSize,
            request.SearchTerm,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

        var dtos = users.Select(user => new PermissionUserDto
        {
            UserId = user.UserId,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            FullName = $"{user.FirstName} {user.LastName}".Trim(),
            DisplayName = user.DisplayName,
            ProfileImageUrl = _imageUrlComposer.Compose(user.ProfileImageUrl),
            Status = user.Status,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            ViaDirect = user.ViaDirect,
            ViaOrganization = user.ViaOrganization,
            ViaRole = user.ViaRole,
            RoleNames = user.RoleNames
        }).ToList();

        _logger.LogDebug(
            "Retrieved {Count} of {Total} users for permission {PermissionId}",
            dtos.Count, totalCount, request.PermissionId);

        return new PagedPermissionUsersDto
        {
            Users = dtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
