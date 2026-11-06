using Auth.Application.Common;
using Auth.Application.Features.Passkeys.Common;
using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;

namespace Auth.Application.Features.Users.ResetUserTwoFactor;

/// <summary>
/// Finds the administrators who could reset an account's second factor right now
/// (M8(4)): the people an account's hard-locked factor is reported to.
/// </summary>
/// <remarks>
/// By the endpoint's own rules, never a second copy of them: the permission
/// <see cref="PermissionCodes.Users.ResetTwoFactor"/> matched with wildcards the
/// way every permission check matches it; the no-amplification rule
/// (<see cref="PermissionGrantGuard"/>) against the account's live platform
/// permissions; and an administrator who is not the account, is active, is not
/// waiting for deletion and has a confirmed address. Two more conditions name what
/// acting takes: <see cref="PermissionCodes.Users.Read"/>, because the message's link
/// opens the account's page in the console, which asks it; and an enabled second
/// factor of their own, because the reset asks the administrator's own recent
/// two-factor sign-in, which an administrator without one can never have. That
/// sign-in itself is checked when they act. That factor is a usable one (S20, D-150-4): while
/// passkeys do not run, an administrator whose only factor is a passkey cannot make that
/// sign-in, so the message is not sent to them.
/// </remarks>
public sealed class TwoFactorResetAdministrators
{
    /// <summary>The most administrators one account's lock is reported to.</summary>
    public const int MaxRecipients = 10;

    private readonly IPermissionRepository _permissionRepository;
    private readonly PermissionGrantGuard _grantGuard;
    private readonly UsableSecondFactors _usableFactors;

    public TwoFactorResetAdministrators(
        IPermissionRepository permissionRepository,
        PermissionGrantGuard grantGuard,
        UsableSecondFactors usableFactors)
    {
        _permissionRepository = permissionRepository;
        _grantGuard = grantGuard;
        _usableFactors = usableFactors;
    }

    /// <summary>
    /// The administrators who could reset <paramref name="accountId"/>'s second
    /// factor now, at most <see cref="MaxRecipients"/>, in a stable order.
    /// </summary>
    public async Task<IReadOnlyList<PlatformAuthorityHolder>> FindAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var candidates = (await _permissionRepository.GetUsersHoldingPlatformPermissionsAsync(cancellationToken))
            .Where(holder => holder.CanActOn(accountId)
                && holder.Holds(PermissionCodes.Users.ResetTwoFactor)
                && holder.Holds(PermissionCodes.Users.Read))
            .ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        // Read once, live: the account's platform permissions, which each
        // administrator's own must cover.
        var accountPermissions = await _permissionRepository.GetUserEffectivePermissionsAsync(
            accountId, cancellationToken);

        var eligible = new List<PlatformAuthorityHolder>(MaxRecipients);
        foreach (var candidate in candidates)
        {
            if (!(await _usableFactors.GetAsync(candidate.UserId, cancellationToken)).HasAny)
            {
                continue;
            }

            var covered = await _grantGuard.EnsureCanGrantAsync(
                candidate.UserId, accountPermissions, cancellationToken);
            if (covered.IsError)
            {
                continue;
            }

            eligible.Add(candidate);
            if (eligible.Count == MaxRecipients)
            {
                break;
            }
        }

        return eligible;
    }
}
