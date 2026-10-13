using Auth.Application.Interfaces;
using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.SetupTwoFactor;

/// <summary>
/// Handler for the setup two-factor authentication command.
/// </summary>
/// <remarks>
/// Setup hands a fresh secret to the caller, so it asks for a recent sign-in
/// first. The secret replaces the pending one in place: the failure count and
/// lock that guessing at enable earned stay, so starting setup again does not
/// clear them, and a factor already in use is never touched. A pending factor at
/// the consecutive-failure limit is not rotated at all (X12 PR B): it accepts no
/// code, so its new secret could never be enabled, and the rotation would move the
/// row's change stamp past the owner's password reset, closing again the gate an
/// administrator's reset waits on (LK4 (4)).
/// </remarks>
public class SetupTwoFactorCommandHandler : IRequestHandler<SetupTwoFactorCommand, ErrorOr<TwoFactorSetupResponse>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly IUserRepository _userRepository;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ITwoFactorSecretProtector _secretProtector;
    private readonly AuthenticatorKeyFactory _keyFactory;
    private readonly FirstFactorEmailProofPolicy _emailProofPolicy;
    private readonly ISecondFactorLockout _secondFactorLockout;
    private readonly ILogger<SetupTwoFactorCommandHandler> _logger;

    public SetupTwoFactorCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        IUserRepository userRepository,
        ITwoFactorStateStore twoFactorStateStore,
        ITwoFactorSecretProtector secretProtector,
        AuthenticatorKeyFactory keyFactory,
        FirstFactorEmailProofPolicy emailProofPolicy,
        ISecondFactorLockout secondFactorLockout,
        ILogger<SetupTwoFactorCommandHandler> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _userRepository = userRepository;
        _twoFactorStateStore = twoFactorStateStore;
        _secretProtector = secretProtector;
        _keyFactory = keyFactory;
        _emailProofPolicy = emailProofPolicy;
        _secondFactorLockout = secondFactorLockout;
        _logger = logger;
    }

    public async Task<ErrorOr<TwoFactorSetupResponse>> Handle(
        SetupTwoFactorCommand request,
        CancellationToken cancellationToken)
    {
        // A recent sign-in, before a secret is generated, stored or returned.
        var session = await _reauthenticationGuard.EnsureRecentSignInAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // A pending factor at the consecutive-failure limit stays as it is: the way
        // out is the owner's password reset, then an administrator's reset.
        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(request.UserId, cancellationToken);
        if (snapshot is { IsEnabled: false } && snapshot.IsHardLocked(_secondFactorLockout.HardLimit))
        {
            _logger.LogWarning(
                "Refused a two-factor setup for user {UserId}: the pending factor accepts no code after the consecutive-failure limit",
                request.UserId);
            return TwoFactorErrors.LockedAdminResetRequired;
        }

        // A new secret, and the QR code that names the account by its address.
        var key = await _keyFactory.CreateAsync(user.Email, cancellationToken);

        // Stored encrypted on the pending row: replaced in place, or inserted when
        // there is none. An enabled factor matches nothing, and the secret is not
        // returned.
        var protectedSecret = await _secretProtector.ProtectAsync(request.UserId, key.Secret, cancellationToken);
        if (!await _twoFactorStateStore.TryStorePendingSecretAsync(request.UserId, protectedSecret, cancellationToken))
        {
            return UserErrors.TwoFactorAlreadyEnabled;
        }

        _logger.LogInformation(
            "Two-factor authentication setup initiated for user {UserId}",
            request.UserId);

        // Only a pending factor reaches here, so this is the account's first: enable
        // will want the emailed code exactly when the rule applies right now. A rule
        // that changes before enable is answered there (TwoFactor.EmailCodeRequired).
        return new TwoFactorSetupResponse(
            Secret: key.Secret,
            QrCodeUri: key.QrCodeUri,
            ManualEntryKey: key.ManualEntryKey,
            EmailCodeRequired: _emailProofPolicy.IsRequired);
    }
}
