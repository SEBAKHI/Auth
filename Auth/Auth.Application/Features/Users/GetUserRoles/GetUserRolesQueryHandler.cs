using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUserRoles;

/// <summary>
/// Handler for getting all roles assigned to a user.
/// </summary>
public class GetUserRolesQueryHandler : IRequestHandler<GetUserRolesQuery, ErrorOr<IReadOnlyList<UserRoleDto>>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<GetUserRolesQueryHandler> _logger;

    public GetUserRolesQueryHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IApplicationRepository applicationRepository,
        ILogger<GetUserRolesQueryHandler> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<UserRoleDto>>> Handle(GetUserRolesQuery request, CancellationToken cancellationToken)
    {
        // Verify user exists
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        var userRoles = await _userRepository.GetUserRolesAsync(request.UserId, cancellationToken);

        // Enrich with role and application names
        var dtos = new List<UserRoleDto>();
        foreach (var userRole in userRoles)
        {
            var dto = new UserRoleDto
            {
                Id = userRole.Id,
                UserId = userRole.UserId,
                RoleId = userRole.RoleId,
                ApplicationId = userRole.ApplicationId,
                CreatedAt = userRole.AssignedAt,
                CreatedBy = userRole.AssignedBy,
                ExpiresAt = userRole.ExpiresAt,
                IsActive = userRole.IsActive
            };

            // Get role name
            var role = await _roleRepository.GetByIdAsync(userRole.RoleId, cancellationToken);
            if (role != null)
            {
                dto.RoleName = role.Name;
                dto.RoleCode = role.Code;
                dto.RoleDescription = role.Description;
            }

            // Get application name if applicable
            if (userRole.ApplicationId.HasValue)
            {
                var app = await _applicationRepository.GetByIdAsync(userRole.ApplicationId.Value, cancellationToken);
                if (app != null)
                {
                    dto.ApplicationName = app.Name;
                    dto.ApplicationCode = app.Code;
                }
            }

            dtos.Add(dto);
        }

        _logger.LogDebug("Retrieved {Count} roles for user {UserId}", dtos.Count, request.UserId);

        // Sort in memory: the sortable fields (role/application name) are
        // enrichment values that don't exist as columns on [UserRoles]. The SQL
        // has no ORDER BY, so default to assignment date for a deterministic order.
        return SortHelper
            .Apply(dtos, request.SortBy ?? SortFields.UserRoles.CreatedAt, request.SortDirection, SortSelectors)
            .ToList();
    }

    private static readonly IReadOnlyDictionary<string, Func<UserRoleDto, object?>> SortSelectors =
        SortHelper.Selectors<UserRoleDto>(
            (SortFields.UserRoles.RoleName, dto => dto.RoleName),
            (SortFields.UserRoles.RoleCode, dto => dto.RoleCode),
            (SortFields.UserRoles.RoleDescription, dto => dto.RoleDescription),
            (SortFields.UserRoles.ApplicationName, dto => dto.ApplicationName),
            (SortFields.UserRoles.ApplicationCode, dto => dto.ApplicationCode),
            (SortFields.UserRoles.IsActive, dto => dto.IsActive),
            (SortFields.UserRoles.ExpiresAt, dto => dto.ExpiresAt),
            (SortFields.UserRoles.CreatedAt, dto => dto.CreatedAt));
}
