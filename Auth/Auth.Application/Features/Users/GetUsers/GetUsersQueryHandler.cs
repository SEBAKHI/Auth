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
    private readonly AccountActionPolicy _accountActionPolicy;
    private readonly IImageUrlComposer _imageUrlComposer;

    public GetUsersQueryHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        AccountActionPolicy accountActionPolicy,
        IImageUrlComposer imageUrlComposer)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _accountActionPolicy = accountActionPolicy;
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

        // The page's platform roles and permissions in one read each, and with a
        // caller, what it may do to each row decided in memory from that read and two
        // more: a fixed number of reads per page, never one per row.
        var pageIds = users.Select(u => u.Id).ToList();
        var pageRoles = await _roleRepository.GetUserRolesForUsersAsync(pageIds, cancellationToken);
        var decisions = request.ActorId is { } actorId
            ? await _accountActionPolicy.DecideForAsync(actorId, pageIds, cancellationToken)
            : null;
        var pagePermissions = decisions is null
            ? await _permissionRepository.GetUsersEffectivePermissionsAsync(pageIds, cancellationToken)
            : null;

        var userDtos = new List<UserDto>();
        foreach (var user in users)
        {
            var roles = pageRoles[user.Id];
            var permissions = decisions?.PermissionsOf(user.Id) ?? pagePermissions![user.Id];
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
                HasPassword = user.PasswordHash is not null,
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
                Permissions = permissions.ToList(),
                AllowedActions = decisions is null ? null : UserAllowedActionsDto.For(decisions, user.Id)
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
