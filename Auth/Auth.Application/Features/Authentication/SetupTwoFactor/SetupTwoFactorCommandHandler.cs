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
/// clear them, and a factor already in use is never touched.
/// </remarks>
public class SetupTwoFactorCommandHandler : IRequestHandler<SetupTwoFactorCommand, ErrorOr<TwoFactorSetupResponse>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly IUserRepository _userRepository;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ITwoFactorSecretProtector _secretProtector;
    private readonly AuthenticatorKeyFactory _keyFactory;
    private readonly FirstFactorEmailProofPolicy _emailProofPolicy;
    private readonly ILogger<SetupTwoFactorCommandHandler> _logger;

    public SetupTwoFactorCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        IUserRepository userRepository,
        ITwoFactorStateStore twoFactorStateStore,
        ITwoFactorSecretProtector secretProtector,
        AuthenticatorKeyFactory keyFactory,
        FirstFactorEmailProofPolicy emailProofPolicy,
        ILogger<SetupTwoFactorCommandHandler> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _userRepository = userRepository;
        _twoFactorStateStore = twoFactorStateStore;
        _secretProtector = secretProtector;
        _keyFactory = keyFactory;
        _emailProofPolicy = emailProofPolicy;
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
