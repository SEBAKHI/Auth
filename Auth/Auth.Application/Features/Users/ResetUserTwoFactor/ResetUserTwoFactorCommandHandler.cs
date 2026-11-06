using Auth.Application.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.ResetUserTwoFactor;

/// <summary>
/// Handler for an administrator's reset of another account's second factor.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The administrator's own session first, before anything about the account
/// is read: recent (<c>TwoFactor:ReauthenticationMaxAgeMinutes</c>) and proved two
/// factors — the rule for changing one's own factor, whatever the enforcement
/// switch says. Without it, a stolen administrator password alone could strip the
/// second factor from every account below that administrator's authority.</item>
/// <item>The account decides what it can decide alone: never the administrator's
/// own, never the system account — before anything about its grants is read.</item>
/// <item>No amplification: the administrator's own platform permissions must cover
/// every platform permission the account holds, by the rule every grant obeys.
/// After a reset the next person to set a factor up owns it, so resetting an
/// account with more authority than one's own would be granting that authority
/// by another name.</item>
/// <item>An account with neither a factor row nor the flag has nothing to reset.
/// A row in any state, or a flag alone, is removed: the reset also repairs an
/// account whose flag and row disagree.</item>
/// <item>A factor at the consecutive-failure limit (the second-factor hard lock)
/// only after its owner reset the password: whoever drove it there knew the
/// password (LK4 (4), <c>TwoFactor.PasswordResetRequiredFirst</c>). Nothing has
/// been revoked when this refuses. The owner's emergency script is not gated: it
/// carries the same rule as a comment for the owner to apply.</item>
/// <item>Every session, refresh token and SSO session of the account is revoked
/// first, so a thief who held the old factor's session is out; then the row and
/// the flag go in one transaction; then the audit row and the email to the owner.
/// In this order a failure leaves the account signed out with its factor intact,
/// and the same request, sent again, completes — never a factor removed while the
/// sessions it protected live on.</item>
/// </list>
/// Every account below the administrator's authority can be reset — every
/// application user and organization owner included, whose grants are not
/// platform permissions — so the endpoint is rate-limited like the self ones.
/// </remarks>
public class ResetUserTwoFactorCommandHandler : IRequestHandler<ResetUserTwoFactorCommand, ErrorOr<Success>>
{
    private const string RevocationReason = "Two-factor authentication reset by an administrator";

    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly IUserRepository _userRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly PermissionGrantGuard _grantGuard;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ICredentialRevocationService _credentialRevocationService;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ISecondFactorLockout _secondFactorLockout;
    private readonly IPublisher _publisher;
    private readonly ITwoFactorBindCodeRepository _bindCodes;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ResetUserTwoFactorCommandHandler> _logger;

    public ResetUserTwoFactorCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        IUserRepository userRepository,
        IPermissionRepository permissionRepository,
        PermissionGrantGuard grantGuard,
        ITwoFactorStateStore twoFactorStateStore,
        ICredentialRevocationService credentialRevocationService,
        IDomainEventDispatcher eventDispatcher,
        ISecondFactorLockout secondFactorLockout,
        IPublisher publisher,
        ITwoFactorBindCodeRepository bindCodes,
        TimeProvider timeProvider,
        ILogger<ResetUserTwoFactorCommandHandler> logger)
    {
        _publisher = publisher;
        _bindCodes = bindCodes;
        _reauthenticationGuard = reauthenticationGuard;
        _userRepository = userRepository;
        _permissionRepository = permissionRepository;
        _grantGuard = grantGuard;
        _twoFactorStateStore = twoFactorStateStore;
        _credentialRevocationService = credentialRevocationService;
        _eventDispatcher = eventDispatcher;
        _secondFactorLockout = secondFactorLockout;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(ResetUserTwoFactorCommand request, CancellationToken cancellationToken)
    {
        // 0. The administrator's own recent two-factor session, before the account
        //    is read at all.
        var actorSession = await _reauthenticationGuard.EnsureRecentTwoFactorSignInAsync(
            request.ResetBy, request.ActorSessionId, cancellationToken);
        if (actorSession.IsError)
        {
            return actorSession.Errors;
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // 1. Never one's own, never the system account.
        var resettable = user.EnsureTwoFactorResettableBy(request.ResetBy);
        if (resettable.IsError)
        {
            _logger.LogWarning(
                "Refused a two-factor reset of user {UserId} by {ResetBy}: the administrator's own account or the system account",
                request.UserId, request.ResetBy);
            return resettable.Errors;
        }

        // 2. No amplification: the account's platform permissions, read live, must
        //    all be ones the administrator holds.
        var targetPermissions = await _permissionRepository.GetUserEffectivePermissionsAsync(
            request.UserId, cancellationToken);
        var covered = await _grantGuard.EnsureCanGrantAsync(request.ResetBy, targetPermissions, cancellationToken);
        if (covered.IsError)
        {
            _logger.LogWarning(
                "Refused a two-factor reset of user {UserId} by {ResetBy}: the account holds platform permissions the administrator does not",
                request.UserId, request.ResetBy);
            return TwoFactorErrors.ResetNotPermitted;
        }

        // 3. Something to reset: a factor row in any state, the flag, or (S20, PK12) a
        //    live passkey that counts as a factor — an administrator whose only factor is a
        //    passkey is reset, not answered "two-factor is not enabled".
        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(request.UserId, cancellationToken);
        if (snapshot is null
            && !user.TwoFactorEnabled
            && !(await _twoFactorStateStore.GetQualifyingFactorsAsync(request.UserId, cancellationToken)).Passkey)
        {
            return UserErrors.TwoFactorNotEnabled;
        }

        // 3b. A factor at the consecutive-failure limit (LK4 (4)): whoever drove it
        //     there knew the password, so it goes only once the owner has reset the
        //     password since — before anything is revoked. Below the limit, and
        //     under the Legacy mode (no limit), nothing changes.
        var gate = snapshot?.EnsureAdministratorMayReset(_secondFactorLockout.HardLimit, user.PasswordChangedAt)
            ?? Result.Success;
        if (gate.IsError)
        {
            _logger.LogWarning(
                "Refused a two-factor reset of user {UserId} by {ResetBy}: the factor is hard-locked and the owner has not reset the password since",
                request.UserId, request.ResetBy);
            return gate.Errors;
        }

        // 4. Everything that might have been signed in with the factor goes first;
        //    then the row and the flag together. From the first write on, a client
        //    that disconnects must not stop it halfway.
        var sessionsEnded = await _credentialRevocationService.RevokeAllCredentialsAsync(
            request.UserId, request.ResetBy, RevocationReason, CancellationToken.None);

        var reset = await _twoFactorStateStore.TryResetAsync(request.UserId, request.ResetBy, CancellationToken.None);
        if (reset is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // Recorded on the aggregate only now, for a change that happened, with how many
        // passkeys went with the factor: the owner's notice says so (REVIEW-FIXES 150 F16).
        user.ResetTwoFactor(request.ResetBy, reset.RevokedPasskeys.Count);

        // REVIEW-FIXES 150 F18 (g): the reset leaves the password as it was, so an emailed code
        // to bind a factor, asked for before it, must not bind one after it. Never failing the
        // reset, which is done.
        try
        {
            await _bindCodes.InvalidateOutstandingForUserAsync(request.UserId, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Two-factor authentication of user {UserId} was reset, but the outstanding codes to bind a second factor could not be ended",
                request.UserId);
        }

        _logger.LogWarning(
            "Two-factor authentication of user {UserId} reset by administrator {ResetBy}; {SessionsEnded} sessions ended, {PasskeysRevoked} passkeys revoked",
            request.UserId, request.ResetBy, sessionsEnded, reset.RevokedPasskeys.Count);

        // The audit row and the email to the owner; then one audit row per passkey the reset
        // revoked (the owner's notice of the reset covers them, so none of their own).
        await _eventDispatcher.DispatchEventsAsync(user, CancellationToken.None);
        foreach (var passkey in reset.RevokedPasskeys)
        {
            await _publisher.Publish(
                new PasskeyRemovedEvent(
                    request.UserId,
                    passkey.Id,
                    passkey.Name,
                    passkey.CreatedAt,
                    PasskeyRemovalReasons.PeerReset,
                    request.ResetBy,
                    user.Email.Value,
                    user.NameToShow,
                    DeviceName: null,
                    passkey.RevokedAt ?? _timeProvider.GetUtcNow().UtcDateTime),
                CancellationToken.None);
        }

        return Result.Success;
    }
}
