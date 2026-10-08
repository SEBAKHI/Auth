using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.SetupTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.BeginAuthenticatorReplacement;

/// <summary>
/// Handler for starting an authenticator replacement: proves the current factor,
/// then issues the new secret, which waits beside the current one until a code
/// from the new app confirms it.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>A recent session that proved two factors, before anything is counted.</item>
/// <item>One attempt counted against the factor, then the code checked: from the
/// current app, or a recovery code when the phone is gone.</item>
/// <item>The new secret is generated and encrypted before the transaction; the
/// proof is settled and the secret stored in one transaction. Nothing changes for
/// the owner yet — the current app keeps working until the confirmation — so no
/// notice is sent here; the confirmation sends it.</item>
/// </list>
/// The waiting secret never counts as the factor: it is in a column of its own,
/// unlike the pending secret of a factor being set up.
/// </remarks>
public class BeginAuthenticatorReplacementCommandHandler
    : IRequestHandler<BeginAuthenticatorReplacementCommand, ErrorOr<TwoFactorSetupResponse>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly ISecondFactorVerifier _secondFactorVerifier;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ITwoFactorSecretProtector _secretProtector;
    private readonly AuthenticatorKeyFactory _keyFactory;
    private readonly TotpReplayPolicy _replayPolicy;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<BeginAuthenticatorReplacementCommandHandler> _logger;

    public BeginAuthenticatorReplacementCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        ISecondFactorVerifier secondFactorVerifier,
        ITwoFactorStateStore twoFactorStateStore,
        ITwoFactorSecretProtector secretProtector,
        AuthenticatorKeyFactory keyFactory,
        TotpReplayPolicy replayPolicy,
        IUserRepository userRepository,
        ILogger<BeginAuthenticatorReplacementCommandHandler> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _secondFactorVerifier = secondFactorVerifier;
        _twoFactorStateStore = twoFactorStateStore;
        _secretProtector = secretProtector;
        _keyFactory = keyFactory;
        _replayPolicy = replayPolicy;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<TwoFactorSetupResponse>> Handle(
        BeginAuthenticatorReplacementCommand request,
        CancellationToken cancellationToken)
    {
        // 1. A recent two-factor session, before anything is read or counted.
        var session = await _reauthenticationGuard.EnsureRecentTwoFactorSignInAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        // 2. One attempt counted, then the code checked against the CURRENT factor.
        var reservation = await _secondFactorVerifier.ReserveAsync(
            request.UserId, expectEnabled: true, cancellationToken);
        if (reservation.IsError)
        {
            return reservation.Errors;
        }

        var method = request.UseRecoveryCode ? SecondFactorMethod.RecoveryCode : SecondFactorMethod.Totp;
        var proof = await _secondFactorVerifier.VerifyAsync(
            reservation.Value, request.Code, method, cancellationToken);
        if (proof.IsError)
        {
            // The reservation stays counted: a wrong code is a failure.
            _logger.LogWarning(
                "Invalid {Method} code while starting an authenticator replacement for user {UserId} from {IpAddress}",
                method, request.UserId, request.IpAddress);
            return proof.Errors;
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // 3. The new secret, generated and encrypted before the transaction.
        var key = await _keyFactory.CreateAsync(user.Email, cancellationToken);
        var protectedSecret = await _secretProtector.ProtectAsync(request.UserId, key.Secret, cancellationToken);

        var outcome = await _twoFactorStateStore.TryBeginReplacementAsync(
            request.UserId,
            proof.Value,
            _replayPolicy.RejectReusedCodes,
            protectedSecret,
            cancellationToken);

        switch (outcome)
        {
            case LoginCommitOutcome.Committed:
                break;

            case LoginCommitOutcome.ReuseAccepted:
                _logger.ReusedCodeAccepted(request.UserId, TotpReplayLog.ReplaceAuthenticator, request.IpAddress);
                break;

            case LoginCommitOutcome.StepReused:
                _logger.ReusedCodeRejected(request.UserId, TotpReplayLog.ReplaceAuthenticator, request.IpAddress);
                return TwoFactorErrors.CodeAlreadyUsed;

            case LoginCommitOutcome.RecoveryCodesChanged:
                // A concurrent sign-in spent the recovery code between the check and
                // the commit. Nothing was written.
                return method == SecondFactorMethod.RecoveryCode
                    ? TwoFactorErrors.InvalidRecoveryCode
                    : TwoFactorErrors.CodeAlreadyUsed;

            default:
                // The factor was switched off or removed meanwhile — or an outcome
                // this handler does not know, which never hands out a secret.
                return UserErrors.TwoFactorNotEnabled;
        }

        _logger.LogInformation(
            "Authenticator replacement started for user {UserId} via {Method}; the new secret waits {Minutes} minutes for its confirming code",
            request.UserId, proof.Value.Method, TwoFactorAuth.PendingReplacementLifetimeMinutes);

        // The setup response's shape, so the app is set up with the same screen.
        // The emailed code belongs to a FIRST factor only: never asked here.
        return new TwoFactorSetupResponse(
            Secret: key.Secret,
            QrCodeUri: key.QrCodeUri,
            ManualEntryKey: key.ManualEntryKey,
            EmailCodeRequired: false);
    }
}
