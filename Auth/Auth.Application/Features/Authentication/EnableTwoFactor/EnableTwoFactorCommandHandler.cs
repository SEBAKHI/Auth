using System.Text.Json;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.EnableTwoFactor;

/// <summary>
/// Handler for the enable two-factor authentication command.
/// </summary>
/// <remarks>
/// Confirms the pending secret with a code from the authenticator app. The code
/// is checked like any other second-factor code — a recent sign-in first, then a
/// counted attempt on the pending factor, so guessing at enable locks it like
/// guessing at sign-in — and the factor row, its recovery codes, the code's time
/// step and the account flag are written in one transaction. Of two concurrent
/// enables only one writes; the other shows no codes.
/// </remarks>
public class EnableTwoFactorCommandHandler : IRequestHandler<EnableTwoFactorCommand, ErrorOr<EnableTwoFactorResponse>>
{
    private const int RecoveryCodeCount = 10;

    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly ISecondFactorVerifier _secondFactorVerifier;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ITotpService _totpService;
    private readonly TotpReplayPolicy _replayPolicy;
    private readonly IUserRepository _userRepository;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<EnableTwoFactorCommandHandler> _logger;

    public EnableTwoFactorCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        ISecondFactorVerifier secondFactorVerifier,
        ITwoFactorStateStore twoFactorStateStore,
        ITotpService totpService,
        TotpReplayPolicy replayPolicy,
        IUserRepository userRepository,
        IDomainEventDispatcher eventDispatcher,
        ILogger<EnableTwoFactorCommandHandler> logger)
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

    public async Task<ErrorOr<EnableTwoFactorResponse>> Handle(
        EnableTwoFactorCommand request,
        CancellationToken cancellationToken)
    {
        // A recent sign-in, before anything is read, counted or written.
        var session = await _reauthenticationGuard.EnsureRecentSignInAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        // Any further proof that binding a factor demands, beyond the code itself,
        // belongs here: after the recent sign-in, before an attempt is reserved.

        // One attempt counted against the pending factor before the code is
        // checked. No pending row answers SetupRequired, an enabled one
        // AlreadyEnabled, a locked one LockedOut — and nothing is checked.
        var reservation = await _secondFactorVerifier.ReserveAsync(
            request.UserId, expectEnabled: false, cancellationToken);
        if (reservation.IsError)
        {
            return reservation.Errors;
        }

        var proof = await _secondFactorVerifier.VerifyAsync(
            reservation.Value, request.Code, SecondFactorMethod.Totp, cancellationToken);
        if (proof.IsError)
        {
            // The reservation stays counted: the fifth wrong code locks the factor.
            _logger.LogWarning(
                "Invalid TOTP code during 2FA enable for user {UserId}",
                request.UserId);
            return proof.Errors;
        }

        var step = proof.Value.Step
            ?? throw new InvalidOperationException("A TOTP proof must carry the time step it matched.");

        // Generated and hashed before the transaction: no hashing inside it.
        var recoveryCodes = _totpService.GenerateRecoveryCodes(RecoveryCodeCount);
        var recoveryCodesJson = JsonSerializer.Serialize(
            recoveryCodes.Select(code => _totpService.HashRecoveryCode(code)).ToArray());

        // Read before the commit, with the request's token: once the factor is on,
        // nothing that follows may be stopped by the caller going away.
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // The factor row — only while it still holds the secret the code was
        // checked against — its codes, the code's step and the account flag, in one
        // transaction.
        var outcome = await _twoFactorStateStore.TryEnableAsync(
            request.UserId,
            reservation.Value.Snapshot.ProtectedSecretKey,
            recoveryCodesJson,
            step,
            _replayPolicy.RejectReusedCodes,
            cancellationToken);

        switch (outcome)
        {
            case LoginCommitOutcome.Committed:
                break;

            case LoginCommitOutcome.ReuseAccepted:
                _logger.ReusedCodeAccepted(request.UserId, TotpReplayLog.Enable, request.IpAddress);
                break;

            case LoginCommitOutcome.AlreadyEnabled:
                // Another tab or device enabled it first; its codes are the stored
                // ones, so these are never shown.
                return UserErrors.TwoFactorAlreadyEnabled;

            default:
                // The pending secret was replaced, or removed, after the code was
                // checked — or an outcome this handler does not know, which never
                // shows codes.
                return TwoFactorErrors.SetupRequired;
        }

        // Recorded on the aggregate only now, for a change that happened.
        user.EnableTwoFactor(request.UserId, session.Value.DeviceName);

        _logger.LogInformation(
            "Two-factor authentication enabled for user {UserId}",
            request.UserId);

        // The audit row and the email to the owner. The factor is already on, so a
        // client that disconnects must not cancel them.
        await _eventDispatcher.DispatchEventsAsync(user, CancellationToken.None);

        return new EnableTwoFactorResponse(recoveryCodes);
    }
}
