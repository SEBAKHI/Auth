using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUserById;

/// <summary>
/// Handler for getting a user by ID.
/// </summary>
public class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, ErrorOr<UserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IImageUrlComposer _imageUrlComposer;

    public GetUserByIdQueryHandler(
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

    public async Task<ErrorOr<UserDto>> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.Id, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.Id);
        }

        var roles = await _roleRepository.GetUserRolesAsync(user.Id, cancellationToken);
        var permissions = await _permissionRepository.GetUserEffectivePermissionsAsync(user.Id, cancellationToken);
        var userNames = await NameLookupHelper.UserNamesAsync(
            _userRepository, [user.CreatedBy, user.ModifiedBy], cancellationToken);

        return new UserDto
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
            Roles = roles.Select(r => r.Code).ToList(),
            Permissions = permissions.ToList()
        };
    }
}
