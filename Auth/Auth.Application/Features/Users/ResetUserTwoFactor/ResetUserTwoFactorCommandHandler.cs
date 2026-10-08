using Auth.Application.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.ResetUserTwoFactor;

/// <summary>
/// Handler for an administrator's reset of another account's second factor.
/// </summary>
/// <remarks>
/// <list type="number">
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
/// <item>The row and the flag go in one transaction; then every session, refresh
/// token and SSO session of the account is revoked, so a thief who held the old
/// factor's session is out too; then the audit row and the email to the owner.</item>
/// </list>
/// No recency or two-factor check on the administrator beyond the permission:
/// while enforcement is on, a token carries platform permissions only if its
/// session proved a second factor.
/// </remarks>
public class ResetUserTwoFactorCommandHandler : IRequestHandler<ResetUserTwoFactorCommand, ErrorOr<Success>>
{
    private const string RevocationReason = "Two-factor authentication reset by an administrator";

    private readonly IUserRepository _userRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly PermissionGrantGuard _grantGuard;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ICredentialRevocationService _credentialRevocationService;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<ResetUserTwoFactorCommandHandler> _logger;

    public ResetUserTwoFactorCommandHandler(
        IUserRepository userRepository,
        IPermissionRepository permissionRepository,
        PermissionGrantGuard grantGuard,
        ITwoFactorStateStore twoFactorStateStore,
        ICredentialRevocationService credentialRevocationService,
        IDomainEventDispatcher eventDispatcher,
        ILogger<ResetUserTwoFactorCommandHandler> logger)
    {
        _userRepository = userRepository;
        _permissionRepository = permissionRepository;
        _grantGuard = grantGuard;
        _twoFactorStateStore = twoFactorStateStore;
        _credentialRevocationService = credentialRevocationService;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(ResetUserTwoFactorCommand request, CancellationToken cancellationToken)
    {
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

        // 3. Something to reset: a factor row in any state, or the flag.
        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(request.UserId, cancellationToken);
        if (snapshot is null && !user.TwoFactorEnabled)
        {
            return UserErrors.TwoFactorNotEnabled;
        }

        // 4. The row and the flag together.
        if (!await _twoFactorStateStore.TryResetAsync(request.UserId, request.ResetBy, cancellationToken))
        {
            return UserErrors.NotFound(request.UserId);
        }

        // The factor is gone: everything that might have been signed in with it
        // goes too. A client that disconnects must not stop it.
        var sessionsEnded = await _credentialRevocationService.RevokeAllCredentialsAsync(
            request.UserId, request.ResetBy, RevocationReason, CancellationToken.None);

        // Recorded on the aggregate only now, for a change that happened.
        user.ResetTwoFactor(request.ResetBy);

        _logger.LogWarning(
            "Two-factor authentication of user {UserId} reset by administrator {ResetBy}; {SessionsEnded} sessions ended",
            request.UserId, request.ResetBy, sessionsEnded);

        // The audit row and the email to the owner.
        await _eventDispatcher.DispatchEventsAsync(user, CancellationToken.None);

        return Result.Success;
    }
}
