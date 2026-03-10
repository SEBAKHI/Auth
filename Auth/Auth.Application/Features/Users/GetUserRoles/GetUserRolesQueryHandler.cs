using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
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
            }

            // Get application name if applicable
            if (userRole.ApplicationId.HasValue)
            {
                var app = await _applicationRepository.GetByIdAsync(userRole.ApplicationId.Value, cancellationToken);
                if (app != null)
                {
                    dto.ApplicationName = app.Name;
                }
            }

            dtos.Add(dto);
        }

        _logger.LogDebug("Retrieved {Count} roles for user {UserId}", dtos.Count, request.UserId);

        return dtos;
    }
}
