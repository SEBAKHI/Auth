using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUsers;

/// <summary>
/// Handler for getting a paginated list of users.
/// </summary>
public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, ErrorOr<PagedUsersDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;

    public GetUsersQueryHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
    }

    public async Task<ErrorOr<PagedUsersDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var (users, totalCount) = await _userRepository.GetPagedAsync(
            request.PageNumber,
            request.PageSize,
            request.SearchTerm,
            cancellationToken);

        var userDtos = new List<UserDto>();
        foreach (var user in users)
        {
            var roles = await _roleRepository.GetUserRolesAsync(user.Id, cancellationToken);
            var permissions = await _permissionRepository.GetUserEffectivePermissionsAsync(user.Id, cancellationToken);
            userDtos.Add(new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                DisplayName = user.DisplayName,
                PhoneNumber = user.PhoneNumber,
                Status = user.Status,
                EmailConfirmed = user.EmailConfirmed,
                PhoneConfirmed = user.PhoneConfirmed,
                TwoFactorEnabled = user.TwoFactorEnabled,
                PreferredLanguage = user.PreferredLanguage,
                TimeZone = user.TimeZone,
                LastLoginAt = user.LastLoginAt,
                CreatedAt = user.CreatedAt,
                ModifiedAt = user.ModifiedAt,
                Roles = roles.Select(r => r.Code).ToList(),
                Permissions = permissions.ToList()
            });
        }

        return new PagedUsersDto
        {
            Users = userDtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
