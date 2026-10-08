using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.DisableTwoFactor;

/// <summary>
/// Handler for the disable two-factor authentication command.
/// </summary>
/// <remarks>
/// Switching the second factor off is what someone holding the password wants
/// most, so it asks for more than a code: a recent sign-in first, then a counted
/// attempt, then the code — from the authenticator app, or a recovery code for a
/// user whose phone is gone. The factor row and the account flag change together,
/// the other sessions are signed out, and the owner is told by email.
/// <para>
/// While <c>TwoFactor:EnforceForPlatformAdmins</c> is on, a platform administrator
/// cannot switch the factor off at all: the factor is what their platform authority
/// rests on. Refused after the recent sign-in and before any attempt is counted.
/// </para>
/// </remarks>
public class DisableTwoFactorCommandHandler : IRequestHandler<DisableTwoFactorCommand, ErrorOr<Success>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly IPlatformMfaPolicy _platformMfaPolicy;
    private readonly ISecondFactorVerifier _secondFactorVerifier;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly TotpReplayPolicy _replayPolicy;
    private readonly IUserRepository _userRepository;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<DisableTwoFactorCommandHandler> _logger;

    public DisableTwoFactorCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        IPlatformMfaPolicy platformMfaPolicy,
        ISecondFactorVerifier secondFactorVerifier,
        ITwoFactorStateStore twoFactorStateStore,
        TotpReplayPolicy replayPolicy,
        IUserRepository userRepository,
        ICredentialRevocationService credentialRevocation,
        IDomainEventDispatcher eventDispatcher,
        ILogger<DisableTwoFactorCommandHandler> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _platformMfaPolicy = platformMfaPolicy;
        _secondFactorVerifier = secondFactorVerifier;
        _twoFactorStateStore = twoFactorStateStore;
        _replayPolicy = replayPolicy;
        _userRepository = userRepository;
        _credentialRevocation = credentialRevocation;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        DisableTwoFactorCommand request,
        CancellationToken cancellationToken)
    {
        // 1. A recent sign-in, before anything is read, counted or written.
        var session = await _reauthenticationGuard.EnsureRecentSignInAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        // 2. A platform administrator under enforcement keeps the factor: refused
        //    before any attempt is counted, so asking costs nothing.
        if (await _platformMfaPolicy.IsEnforcedForUserAsync(request.UserId, cancellationToken))
        {
            _logger.LogInformation(
                "Switching two-factor off was refused for user {UserId}: platform administrators must keep it",
                request.UserId);
            return TwoFactorErrors.RequiredByPolicy;
        }

        // 3. One attempt counted against the factor before the code is checked: a
        //    locked factor checks nothing, and the fifth wrong code locks it.
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
                "Invalid {Method} code during 2FA disable for user {UserId}",
                method, request.UserId);
            return proof.Errors;
        }

        // Read before the commit, with the request's token: once the factor is off,
        // nothing that follows may be stopped by the caller going away.
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // 4. The factor row and the account flag, in one transaction.
        var outcome = await _twoFactorStateStore.TryDisableAsync(
            request.UserId, proof.Value, _replayPolicy.RejectReusedCodes, cancellationToken);

        switch (outcome)
        {
            case LoginCommitOutcome.Committed:
                break;

            case LoginCommitOutcome.ReuseAccepted:
                _logger.ReusedCodeAccepted(request.UserId, TotpReplayLog.Disable, request.IpAddress);
                break;

            case LoginCommitOutcome.StepReused:
                // Refused like a wrong code, and counted like one: the reservation stays.
                _logger.ReusedCodeRejected(request.UserId, TotpReplayLog.Disable, request.IpAddress);
                return TwoFactorErrors.CodeAlreadyUsed;

            case LoginCommitOutcome.RecoveryCodesChanged:
                // A concurrent sign-in spent a code between the check and the commit.
                return TwoFactorErrors.InvalidRecoveryCode;

            default:
                // The factor was switched off or removed meanwhile — or an outcome
                // this handler does not know, which never switches anything off.
                return UserErrors.TwoFactorNotEnabled;
        }

        // 5. Recorded on the aggregate only now, for a change that happened.
        user.DisableTwoFactor(request.UserId, session.Value.DeviceName);

        _logger.LogInformation(
            "Two-factor authentication disabled for user {UserId}",
            request.UserId);

        // 6. Sign out every other session and browser before the events run. The
        //    factor is already off, so neither this nor the notice may be undone by
        //    a client that disconnects: both run without the request's token. A
        //    failed revocation must not cost the owner the email or the request its
        //    success; it is logged for an operator, and those sessions live on until
        //    they expire.
        try
        {
            await _credentialRevocation.RevokeCredentialsAsync(
                request.UserId,
                request.CurrentSessionId,
                request.IdpSessionToken,
                revokedBy: request.UserId,
                "Two-factor disabled",
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Two-factor authentication was disabled for user {UserId} but the other sessions could not be signed out",
                request.UserId);
        }

        // 7. The audit row and the email to the owner.
        await _eventDispatcher.DispatchEventsAsync(user, CancellationToken.None);

        return Result.Success;
    }
}
