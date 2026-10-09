using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ConfirmAuthenticatorReplacement;

/// <summary>
/// Handler for confirming an authenticator replacement: a code from the new app
/// makes the waiting secret the factor's secret, with a new set of recovery codes.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>A recent session that proved two factors, before anything is counted.</item>
/// <item>No replacement waiting — none started, or older than
/// <see cref="Domain.Entities.TwoFactorAuth.PendingReplacementLifetimeMinutes"/> —
/// answers <c>TwoFactor.NoPendingReplacement</c> with nothing counted.</item>
/// <item>One attempt counted against the factor, then the code checked against the
/// WAITING secret, through the same check every authenticator code takes.</item>
/// <item>One statement makes the swap, only while the waiting secret is still the
/// one the code was checked against and young enough by the database's clock, and
/// the recovery codes are the set this request saw; it clears the waiting secret,
/// so the confirmation happens once, and claims the new app's step, so the
/// confirming code cannot sign in again.</item>
/// <item>Every other session and browser is signed out, as switching two-factor off
/// does: moving to a new app usually means the old one may be in other hands, and
/// whoever signed in with it must not stay signed in. This session and its SSO
/// cookie stay. (New recovery codes sign nobody out: running low is their usual
/// reason.)</item>
/// </list>
/// The session is not upgraded: it proved two factors already, which is the
/// precondition (an amendment of the plan's M2, recorded in the pull request).
/// </remarks>
public class ConfirmAuthenticatorReplacementCommandHandler
    : IRequestHandler<ConfirmAuthenticatorReplacementCommand, ErrorOr<TwoFactorRecoveryCodesResponse>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly ISecondFactorVerifier _secondFactorVerifier;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ITotpService _totpService;
    private readonly IUserRepository _userRepository;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConfirmAuthenticatorReplacementCommandHandler> _logger;

    public ConfirmAuthenticatorReplacementCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        ISecondFactorVerifier secondFactorVerifier,
        ITwoFactorStateStore twoFactorStateStore,
        ITotpService totpService,
        IUserRepository userRepository,
        ICredentialRevocationService credentialRevocation,
        IDomainEventDispatcher eventDispatcher,
        TimeProvider timeProvider,
        ILogger<ConfirmAuthenticatorReplacementCommandHandler> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _secondFactorVerifier = secondFactorVerifier;
        _twoFactorStateStore = twoFactorStateStore;
        _totpService = totpService;
        _userRepository = userRepository;
        _credentialRevocation = credentialRevocation;
        _eventDispatcher = eventDispatcher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ErrorOr<TwoFactorRecoveryCodesResponse>> Handle(
        ConfirmAuthenticatorReplacementCommand request,
        CancellationToken cancellationToken)
    {
        // 1. A recent two-factor session, before anything is read or counted.
        var session = await _reauthenticationGuard.EnsureRecentTwoFactorSignInAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        // 2. A replacement waiting, before anything is counted: a request with
        //    nothing to confirm can never succeed, so it costs no attempt.
        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(request.UserId, cancellationToken);
        if (snapshot is not { IsEnabled: true })
        {
            return UserErrors.TwoFactorNotEnabled;
        }

        if (!snapshot.HasPendingReplacement(_timeProvider.GetUtcNow().UtcDateTime))
        {
            return TwoFactorErrors.NoPendingReplacement;
        }

        // 3. One attempt counted against the factor, then the code checked against
        //    the waiting secret as this reservation read it.
        var reservation = await _secondFactorVerifier.ReserveAsync(
            request.UserId, expectEnabled: true, cancellationToken);
        if (reservation.IsError)
        {
            return reservation.Errors;
        }

        var proof = await _secondFactorVerifier.VerifyReplacementAsync(
            reservation.Value, request.Code, cancellationToken);
        if (proof.IsError)
        {
            // The reservation stays counted: a wrong code is a failure.
            _logger.LogWarning(
                "Invalid code from the new authenticator for user {UserId} from {IpAddress}",
                request.UserId, request.IpAddress);
            return proof.Errors;
        }

        var step = proof.Value.Step
            ?? throw new InvalidOperationException("A TOTP proof must carry the time step it matched.");

        // Generated and hashed before the statement: no hashing waits on it.
        var (recoveryCodes, recoveryCodesJson) = _totpService.IssueRecoveryCodes();

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // 4. The swap: the waiting secret exactly as the code was checked against it.
        var outcome = await _twoFactorStateStore.TryConfirmReplacementAsync(
            request.UserId,
            reservation.Value.Snapshot.PendingSecretKey!,
            step,
            reservation.Value.Snapshot.RecoveryCodes,
            recoveryCodesJson,
            cancellationToken);

        switch (outcome)
        {
            case LoginCommitOutcome.Committed:
                break;

            case LoginCommitOutcome.ChallengeLost:
                // Confirmed by another request, replaced by a newer start, expired,
                // or the recovery codes were renewed, between the check and the
                // statement. Nothing changed; starting again always works.
                return TwoFactorErrors.NoPendingReplacement;

            default:
                // The factor was switched off or removed meanwhile — or an outcome
                // this handler does not know, which never shows codes.
                return UserErrors.TwoFactorNotEnabled;
        }

        // Recorded on the aggregate only now, for a change that happened.
        user.ReplaceTwoFactorAuthenticator(request.UserId, session.Value.DeviceName);

        _logger.LogInformation(
            "Authenticator replaced for user {UserId}",
            request.UserId);

        // Every other session and browser out, as disable does. The secret is
        // already replaced, so neither this nor the notice may be undone by a
        // client that disconnects; a failed revocation must not cost the owner the
        // codes or the email: it is logged for an operator.
        try
        {
            await _credentialRevocation.RevokeCredentialsAsync(
                request.UserId,
                request.CurrentSessionId,
                request.IdpSessionToken,
                revokedBy: request.UserId,
                "Authenticator replaced",
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "The authenticator was replaced for user {UserId} but the other sessions could not be signed out",
                request.UserId);
        }

        // The audit row and the email to the owner. The secret is already
        // replaced, so a client that disconnects must not cancel them.
        await _eventDispatcher.DispatchEventsAsync(user, CancellationToken.None);

        return new TwoFactorRecoveryCodesResponse(recoveryCodes);
    }
}
