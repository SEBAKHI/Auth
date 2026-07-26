using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
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
    private readonly IImageUrlComposer _imageUrlComposer;

    public GetUsersQueryHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IImageUrlComposer imageUrlComposer)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _imageUrlComposer = imageUrlComposer;
    }

    public async Task<ErrorOr<PagedUsersDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var (users, totalCount) = await _userRepository.GetPagedAsync(
            request.PageNumber,
            request.PageSize,
            request.SearchTerm,
            request.SortBy,
            request.SortDirection,
            request.IncludeDeleted,
            cancellationToken);

        var userNames = await NameLookupHelper.UserNamesAsync(
            _userRepository,
            users.SelectMany(u => new Guid?[] { u.CreatedBy, u.ModifiedBy }),
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
                ProfileImageUrl = _imageUrlComposer.Compose(user.ProfileImageUrl),
                Status = user.Status,
                EmailConfirmed = user.EmailConfirmed,
                PhoneConfirmed = user.PhoneConfirmed,
                TwoFactorEnabled = user.TwoFactorEnabled,
                PreferredLanguage = user.PreferredLanguage,
                TimeZone = user.TimeZone,
                Theme = user.Theme,
                LastLoginAt = user.LastLoginAt,
                FailedLoginAttempts = user.FailedLoginAttempts,
                LockoutEnd = user.LockoutEnd,
                LastLoginIp = user.LastLoginIp,
                PasswordChangedAt = user.PasswordChangedAt,
                PasswordExpiresUtc = user.PasswordExpiresUtc,
                MustChangePassword = user.MustChangePassword,
                CreatedAt = user.CreatedAt,
                CreatedBy = user.CreatedBy,
                CreatedByName = userNames.GetValueOrDefault(user.CreatedBy),
                ModifiedAt = user.ModifiedAt,
                ModifiedBy = user.ModifiedBy,
                ModifiedByName = user.ModifiedBy.HasValue
                    ? userNames.GetValueOrDefault(user.ModifiedBy.Value)
                    : null,
                IsDeleted = user.IsDeleted,
                DeletedAt = user.DeletedAt,
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
