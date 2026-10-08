using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.StepUpTwoFactor;

/// <summary>
/// Handler for the step-up command: proves the second factor inside the current
/// session, so the session counts as two-factor from its next refresh on.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The session first, with nothing counted: a token with no live session row,
/// or a session whose first factor is unknown, cannot be upgraded into two factors —
/// it signs in again, which records both.</item>
/// <item>A session that already proved two factors answers success without a code:
/// another tab stepped up a moment ago.</item>
/// <item>Then one attempt is counted against the factor before the code is checked,
/// as at sign-in, so guessing here locks the factor after five tries.</item>
/// <item>The factor is settled and the session upgraded in one transaction: the
/// TOTP step is claimed (so the code cannot be used again) or the recovery code
/// spent, and the method is OR-ed into the session row and its SSO session.</item>
/// <item>The audit row follows the commit. No email: nothing about the account
/// changed.</item>
/// </list>
/// </remarks>
public class StepUpTwoFactorCommandHandler : IRequestHandler<StepUpTwoFactorCommand, ErrorOr<Success>>
{
    private readonly IUserSessionRepository _sessionRepository;
    private readonly ISecondFactorVerifier _secondFactorVerifier;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly TotpReplayPolicy _replayPolicy;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly IPublisher _publisher;
    private readonly ILogger<StepUpTwoFactorCommandHandler> _logger;

    public StepUpTwoFactorCommandHandler(
        IUserSessionRepository sessionRepository,
        ISecondFactorVerifier secondFactorVerifier,
        ITwoFactorStateStore twoFactorStateStore,
        TotpReplayPolicy replayPolicy,
        IRefreshTokenKeyService refreshTokenKeyService,
        IPublisher publisher,
        ILogger<StepUpTwoFactorCommandHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _secondFactorVerifier = secondFactorVerifier;
        _twoFactorStateStore = twoFactorStateStore;
        _replayPolicy = replayPolicy;
        _refreshTokenKeyService = refreshTokenKeyService;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        StepUpTwoFactorCommand request,
        CancellationToken cancellationToken)
    {
        // 1. The session, before anything is counted.
        if (request.CurrentSessionId is not { } sessionId)
        {
            return Refuse(request.UserId, "the token names no session");
        }

        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null || session.UserId != request.UserId)
        {
            return Refuse(request.UserId, "no session row for the token");
        }

        if (!session.IsActive)
        {
            return Refuse(request.UserId, "the session has ended");
        }

        // 2. Already two factors: another tab stepped up. Nothing to check.
        if (session.Methods.IsMfaSatisfied)
        {
            return Result.Success;
        }

        if (!session.Methods.HasPrimary)
        {
            return Refuse(request.UserId, "the session's first factor is unknown");
        }

        // 3. One attempt counted against the factor before the code is checked. No
        //    enabled factor answers TwoFactorNotEnabled (set one up instead), a
        //    locked one LockedOut — and nothing is checked.
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
                "Invalid {Method} code during two-factor step-up for user {UserId} from {IpAddress}",
                method, request.UserId, request.IpAddress);
            return proof.Errors;
        }

        // 4. The factor and the session, in one transaction.
        var sessionUpgrade = new SessionUpgrade(
            session.Id,
            string.IsNullOrEmpty(request.IdpSessionToken)
                ? null
                : _refreshTokenKeyService.ComputeTokenHash(request.IdpSessionToken),
            AuthenticationMethods.From(proof.Value.Method));

        var outcome = await _twoFactorStateStore.TryCommitStepUpAsync(
            request.UserId, proof.Value, _replayPolicy.RejectReusedCodes, sessionUpgrade, cancellationToken);

        switch (outcome)
        {
            case LoginCommitOutcome.Committed:
                break;

            case LoginCommitOutcome.ReuseAccepted:
                _logger.ReusedCodeAccepted(request.UserId, TotpReplayLog.StepUp, request.IpAddress);
                break;

            case LoginCommitOutcome.StepReused:
                // Refused like a wrong code, and counted like one: the reservation
                // stays. Usually the code that signed in, typed again in another tab.
                _logger.ReusedCodeRejected(request.UserId, TotpReplayLog.StepUp, request.IpAddress);
                return TwoFactorErrors.CodeAlreadyUsed;

            case LoginCommitOutcome.RecoveryCodesChanged:
                // A concurrent sign-in spent a code between the check and the commit.
                return TwoFactorErrors.InvalidRecoveryCode;

            case LoginCommitOutcome.SessionLost:
                return Refuse(request.UserId, "the session ended before the commit");

            default:
                // The factor was switched off or removed meanwhile — or an outcome
                // this handler does not know, which never upgrades anything.
                return UserErrors.TwoFactorNotEnabled;
        }

        _logger.LogInformation(
            "Two-factor step-up completed for user {UserId}, session {SessionId}, via {Method}",
            request.UserId, session.Id, proof.Value.Method);

        // The audit row. The session is already upgraded, so a client that
        // disconnects must not cancel it.
        await _publisher.Publish(
            new TwoFactorSteppedUpEvent(request.UserId, session.Id, proof.Value.Method),
            CancellationToken.None);

        return Result.Success;
    }

    private Error Refuse(Guid userId, string reason)
    {
        _logger.LogInformation(
            "A two-factor step-up for user {UserId} asked for a fresh sign-in: {Reason}",
            userId, reason);

        return AuthErrors.ReauthenticationRequired;
    }
}
