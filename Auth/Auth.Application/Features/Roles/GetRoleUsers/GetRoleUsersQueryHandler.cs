using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.GetRoleUsers;

/// <summary>
/// Handler for getting paginated users assigned a role.
/// </summary>
public class GetRoleUsersQueryHandler : IRequestHandler<GetRoleUsersQuery, ErrorOr<PagedRoleUsersDto>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<GetRoleUsersQueryHandler> _logger;

    public GetRoleUsersQueryHandler(
        IRoleRepository roleRepository,
        ILogger<GetRoleUsersQueryHandler> logger)
    {
        _roleRepository = roleRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<PagedRoleUsersDto>> Handle(
        GetRoleUsersQuery request,
        CancellationToken cancellationToken)
    {
        // Verify role exists
        var role = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (role == null)
        {
            return RoleErrors.NotFound(request.RoleId);
        }

        var (users, totalCount) = await _roleRepository.GetUsersPagedAsync(
            request.RoleId,
            request.PageNumber,
            request.PageSize,
            request.SearchTerm,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

        var dtos = users.Select(user => new RoleUserDto
        {
            UserId = user.UserId,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            FullName = $"{user.FirstName} {user.LastName}".Trim(),
            DisplayName = user.DisplayName,
            Status = user.Status,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            AssignmentSource = user switch
            {
                { ViaDirect: true, ViaOrganization: true } => "both",
                { ViaOrganization: true } => "organization",
                _ => "direct"
            },
            OrganizationNames = user.OrganizationNames
        }).ToList();

        _logger.LogDebug(
            "Retrieved {Count} of {Total} users for role {RoleId}",
            dtos.Count, totalCount, request.RoleId);

        return new PagedRoleUsersDto
        {
            Users = dtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
