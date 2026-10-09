using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.RegenerateRecoveryCodes;

/// <summary>
/// Handler for the regenerate-recovery-codes command: a new set replaces the old
/// one, which stops working.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>A recent session that proved two factors, before anything is counted:
/// whoever holds only the password, or a session that signed in long ago, cannot
/// take a fresh set of codes — the codes are a second factor of their own.</item>
/// <item>One attempt counted against the factor, then the code checked, as at
/// sign-in: a code from the authenticator app or one of the current recovery codes.</item>
/// <item>The new set is generated and hashed before the transaction; then the
/// proof is settled (the step claimed, or the code spent) and the new set written
/// in one transaction, the set only while it is the one this request saw — so of
/// two concurrent requests only one set survives, and only its codes are shown.</item>
/// <item>The audit row and the email to the owner follow the commit.</item>
/// </list>
/// </remarks>
public class RegenerateRecoveryCodesCommandHandler
    : IRequestHandler<RegenerateRecoveryCodesCommand, ErrorOr<TwoFactorRecoveryCodesResponse>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly ISecondFactorVerifier _secondFactorVerifier;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ITotpService _totpService;
    private readonly TotpReplayPolicy _replayPolicy;
    private readonly IUserRepository _userRepository;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<RegenerateRecoveryCodesCommandHandler> _logger;

    public RegenerateRecoveryCodesCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        ISecondFactorVerifier secondFactorVerifier,
        ITwoFactorStateStore twoFactorStateStore,
        ITotpService totpService,
        TotpReplayPolicy replayPolicy,
        IUserRepository userRepository,
        IDomainEventDispatcher eventDispatcher,
        ILogger<RegenerateRecoveryCodesCommandHandler> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _secondFactorVerifier = secondFactorVerifier;
        _twoFactorStateStore = twoFactorStateStore;
        _totpService = totpService;
        _replayPolicy = replayPolicy;
        _userRepository = userRepository;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<TwoFactorRecoveryCodesResponse>> Handle(
        RegenerateRecoveryCodesCommand request,
        CancellationToken cancellationToken)
    {
        // 1. A recent two-factor session, before anything is read or counted.
        var session = await _reauthenticationGuard.EnsureRecentTwoFactorSignInAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        // 2. One attempt counted, then the code checked. No enabled factor answers
        //    TwoFactorNotEnabled, a locked one LockedOut — and nothing is checked.
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
                "Invalid {Method} code while regenerating recovery codes for user {UserId} from {IpAddress}",
                method, request.UserId, request.IpAddress);
            return proof.Errors;
        }

        // 3. Generated and hashed before the transaction: no hashing inside it.
        var (recoveryCodes, recoveryCodesJson) = _totpService.IssueRecoveryCodes();

        // Read before the commit, with the request's token: once the codes are
        // replaced, nothing that follows may be stopped by the caller going away.
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        var outcome = await _twoFactorStateStore.TryRegenerateCodesAsync(
            request.UserId,
            proof.Value,
            _replayPolicy.RejectReusedCodes,
            reservation.Value.Snapshot.RecoveryCodes,
            recoveryCodesJson,
            cancellationToken);

        switch (outcome)
        {
            case LoginCommitOutcome.Committed:
                break;

            case LoginCommitOutcome.ReuseAccepted:
                _logger.ReusedCodeAccepted(request.UserId, TotpReplayLog.RegenerateRecoveryCodes, request.IpAddress);
                break;

            case LoginCommitOutcome.StepReused:
                // Refused like a wrong code, and counted like one.
                _logger.ReusedCodeRejected(request.UserId, TotpReplayLog.RegenerateRecoveryCodes, request.IpAddress);
                return TwoFactorErrors.CodeAlreadyUsed;

            case LoginCommitOutcome.RecoveryCodesChanged:
                // Another request spent the code, or replaced the set, between the
                // check and the commit. Nothing was written; these codes are never shown.
                return method == SecondFactorMethod.RecoveryCode
                    ? TwoFactorErrors.InvalidRecoveryCode
                    : TwoFactorErrors.CodeAlreadyUsed;

            default:
                // The factor was switched off or removed meanwhile — or an outcome
                // this handler does not know, which never shows codes.
                return UserErrors.TwoFactorNotEnabled;
        }

        // Recorded on the aggregate only now, for a change that happened.
        user.RegenerateTwoFactorRecoveryCodes(request.UserId, session.Value.DeviceName);

        _logger.LogInformation(
            "Recovery codes regenerated for user {UserId} via {Method}",
            request.UserId, proof.Value.Method);

        // The audit row and the email to the owner. The codes are already
        // replaced, so a client that disconnects must not cancel them.
        await _eventDispatcher.DispatchEventsAsync(user, CancellationToken.None);

        return new TwoFactorRecoveryCodesResponse(recoveryCodes);
    }
}
