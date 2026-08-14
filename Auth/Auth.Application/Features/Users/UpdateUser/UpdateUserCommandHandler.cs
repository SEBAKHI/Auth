using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.UpdateUser;

/// <summary>
/// Handler for updating an existing user.
/// </summary>
public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, ErrorOr<UserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILogger<UpdateUserCommandHandler> _logger;

    public UpdateUserCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        ILogger<UpdateUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<UserDto>> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.Id, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.Id);
        }

        // Update profile
        user.UpdateProfile(
            firstName: request.FirstName,
            lastName: request.LastName,
            phoneNumber: request.PhoneNumber,
            preferredLanguage: request.PreferredLanguage,
            timeZone: request.TimeZone,
            // Admin updates omit the theme, so a null must not wipe the user's choice.
            theme: request.Theme ?? user.Theme,
            modifiedBy: request.ModifiedBy);

        await _userRepository.UpdateAsync(user, cancellationToken);

        // Get user roles and permissions
        var roles = await _roleRepository.GetUserRolesAsync(user.Id, cancellationToken);
        var roleNames = roles.Select(r => r.Code).ToList();
        var permissions = await _permissionRepository.GetUserEffectivePermissionsAsync(user.Id, cancellationToken);

        _logger.LogInformation(
            "User updated: {UserId} by {ModifiedBy}",
            user.Id, request.ModifiedBy);

        return new UserDto
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
            Theme = user.Theme,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            ModifiedAt = user.ModifiedAt,
            Roles = roleNames,
            Permissions = permissions.ToList()
        };
    }
}
