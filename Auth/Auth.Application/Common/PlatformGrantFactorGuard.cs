using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth.Application.Common;

/// <summary>
/// While <c>TwoFactor:EnforceForPlatformAdmins</c> is on, platform authority goes
/// only to accounts that already have their own second factor.
/// </summary>
/// <remarks>
/// <para>
/// Under enforcement a platform administrator without a factor is asked to set
/// one up — and the first person to do so owns it. Granting platform authority
/// to an account that has no factor would therefore hand it to whoever sets a
/// factor up first: anyone holding that account's password, or able to reset it
/// by email. So the grant waits until the account has its factor.
/// </para>
/// <para>
/// Platform scope only — the scope the platform token reads. Application and
/// organization grants, application tokens and <c>org_perm</c> are never touched:
/// two-step verification stays optional for application users. With enforcement
/// off nothing is read.
/// </para>
/// </remarks>
public class PlatformGrantFactorGuard
{
    private readonly IPlatformMfaPolicy _platformMfaPolicy;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ILogger<PlatformGrantFactorGuard> _logger;

    public PlatformGrantFactorGuard(
        IPlatformMfaPolicy platformMfaPolicy,
        ITwoFactorStateStore twoFactorStateStore,
        ILogger<PlatformGrantFactorGuard> logger)
    {
        _platformMfaPolicy = platformMfaPolicy;
        _twoFactorStateStore = twoFactorStateStore;
        _logger = logger;
    }

    /// <summary>
    /// For a grant to one account — a role assigned, or a permission granted
    /// directly: refused when it is made at platform scope, carries at least one
    /// active permission, enforcement is on and the account has no enabled factor.
    /// </summary>
    /// <param name="userId">The account receiving the grant.</param>
    /// <param name="atPlatformScope">
    /// Whether the grant reaches the platform token: no application on the
    /// assignment, and — for a role — a role that belongs to no application.
    /// </param>
    /// <param name="grantedPermissions">The permissions the grant hands over.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ErrorOr<Success>> EnsureAccountMayReceiveAsync(
        Guid userId,
        bool atPlatformScope,
        IEnumerable<Permission> grantedPermissions,
        CancellationToken cancellationToken)
    {
        if (!atPlatformScope || !grantedPermissions.Any(permission => permission.IsActive))
        {
            return Result.Success;
        }

        if (!_platformMfaPolicy.IsEnforcing)
        {
            return Result.Success;
        }

        if (await _twoFactorStateStore.HasEnabledFactorAsync(userId, cancellationToken))
        {
            return Result.Success;
        }

        _logger.LogWarning(
            "Refused a platform grant to user {UserId}: the account has no enabled second factor while TwoFactor:EnforceForPlatformAdmins is on",
            userId);
        return TwoFactorErrors.RequiredForPlatformGrant;
    }

    /// <summary>
    /// For a permission added to a role: refused when the role is a platform role,
    /// the permission is active, enforcement is on and the role is held at
    /// platform scope by an account without an enabled factor — every holder
    /// receives what the role gains.
    /// </summary>
    /// <param name="role">The role gaining the permission.</param>
    /// <param name="permission">The permission it gains.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ErrorOr<Success>> EnsureRoleHoldersMayReceiveAsync(
        Role role,
        Permission permission,
        CancellationToken cancellationToken)
    {
        if (role.ApplicationId is not null || !permission.IsActive)
        {
            return Result.Success;
        }

        if (!_platformMfaPolicy.IsEnforcing)
        {
            return Result.Success;
        }

        if (!await _twoFactorStateStore.HasPlatformRoleHolderWithoutFactorAsync(role.Id, cancellationToken))
        {
            return Result.Success;
        }

        _logger.LogWarning(
            "Refused adding {PermissionCode} to role {RoleId}: an account holding the role at platform scope has no enabled second factor while TwoFactor:EnforceForPlatformAdmins is on",
            permission.Code.Value, role.Id);
        return TwoFactorErrors.RequiredForPlatformGrant;
    }
}
