using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Users.GetUserById;

/// <summary>
/// Handler for getting a user by ID.
/// </summary>
public class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, ErrorOr<UserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly AccountActionPolicy _accountActionPolicy;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly IOptionsMonitor<EmailSettings> _emailSettings;

    public GetUserByIdQueryHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        AccountActionPolicy accountActionPolicy,
        IImageUrlComposer imageUrlComposer,
        ITwoFactorStateStore twoFactorStateStore,
        IOptionsMonitor<EmailSettings> emailSettings)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _accountActionPolicy = accountActionPolicy;
        _imageUrlComposer = imageUrlComposer;
        _twoFactorStateStore = twoFactorStateStore;
        _emailSettings = emailSettings;
    }

    public async Task<ErrorOr<UserDto>> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.Id, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.Id);
        }

        var roles = await _roleRepository.GetUserRolesAsync(user.Id, cancellationToken);

        // What the caller may do here, by the rules the actions enforce. The same read
        // gives the account's platform permissions, so they are not read twice.
        var decisions = request.ActorId is { } actorId
            ? await _accountActionPolicy.DecideForAsync(actorId, [user.Id], cancellationToken)
            : null;
        var permissions = decisions?.PermissionsOf(user.Id)
            ?? await _permissionRepository.GetUserEffectivePermissionsAsync(user.Id, cancellationToken);
        var userNames = await NameLookupHelper.UserNamesAsync(
            _userRepository, [user.CreatedBy, user.ModifiedBy], cancellationToken);

        // Which second factor the account holds, from its factor row (FA12): the
        // account flag says only that a sign-in asks for one.
        var factor = await _twoFactorStateStore.GetSnapshotAsync(user.Id, cancellationToken);

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
            TotpEnabled = factor?.IsEnabled == true,
            EmailCodeEnabled = factor?.HasEmailCode == true,
            QualifyingMfa = factor?.IsEnabled == true,
            EmailCodeAvailable = _emailSettings.CurrentValue.Enabled,
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
            HasPassword = user.PasswordHash is not null,
            CreatedAt = user.CreatedAt,
            CreatedBy = user.CreatedBy,
            CreatedByName = userNames.GetValueOrDefault(user.CreatedBy),
            ModifiedAt = user.ModifiedAt,
            ModifiedBy = user.ModifiedBy,
            ModifiedByName = user.ModifiedBy.HasValue
                ? userNames.GetValueOrDefault(user.ModifiedBy.Value)
                : null,
            Roles = roles.Select(r => r.Code).ToList(),
            Permissions = permissions.ToList(),
            AllowedActions = decisions is null ? null : UserAllowedActionsDto.For(decisions, user.Id)
        };
    }
}
