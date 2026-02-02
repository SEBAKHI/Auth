using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.DTOs;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.UserManagement.Queries;

/// <summary>
/// Handler for getting all direct permissions granted to a user.
/// </summary>
public class GetUserPermissionsQueryHandler : IRequestHandler<GetUserPermissionsQuery, ErrorOr<IReadOnlyList<UserPermissionDto>>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<GetUserPermissionsQueryHandler> _logger;

    public GetUserPermissionsQueryHandler(
        IUserRepository userRepository,
        IPermissionRepository permissionRepository,
        IApplicationRepository applicationRepository,
        ILogger<GetUserPermissionsQueryHandler> logger)
    {
        _userRepository = userRepository;
        _permissionRepository = permissionRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<UserPermissionDto>>> Handle(GetUserPermissionsQuery request, CancellationToken cancellationToken)
    {
        // Verify user exists
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        var userPermissions = await _userRepository.GetUserPermissionsAsync(request.UserId, cancellationToken);

        // Enrich with permission and application names
        var dtos = new List<UserPermissionDto>();
        foreach (var userPerm in userPermissions)
        {
            var dto = new UserPermissionDto
            {
                Id = userPerm.Id,
                UserId = userPerm.UserId,
                PermissionId = userPerm.PermissionId,
                ApplicationId = userPerm.ApplicationId,
                CreatedAt = userPerm.GrantedAt,
                CreatedBy = userPerm.GrantedBy,
                ExpiresAt = userPerm.ExpiresAt,
                IsActive = userPerm.IsActive
            };

            // Get permission info
            var permission = await _permissionRepository.GetByIdAsync(userPerm.PermissionId, cancellationToken);
            if (permission != null)
            {
                dto.PermissionName = permission.Name;
                dto.PermissionCode = permission.Code;
            }

            // Get application name if applicable
            if (userPerm.ApplicationId.HasValue)
            {
                var app = await _applicationRepository.GetByIdAsync(userPerm.ApplicationId.Value, cancellationToken);
                if (app != null)
                {
                    dto.ApplicationName = app.Name;
                }
            }

            dtos.Add(dto);
        }

        _logger.LogDebug("Retrieved {Count} direct permissions for user {UserId}", dtos.Count, request.UserId);

        return dtos;
    }
}
