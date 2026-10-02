using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.VerifyTwoFactorLogin;

/// <summary>
/// Handler that completes a two-factor login: validates the pending challenge,
/// reserves the attempt on the account and then on the challenge, verifies the
/// TOTP or recovery code, and only then issues tokens via the shared login
/// response builder.
/// </summary>
/// <remarks>
/// Every limit here is enforced by a conditional write, not by what a read saw.
/// A burst of concurrent guesses all read the same counts, so a check on a read
/// lets every one of them through; a reservation lets through exactly as many as
/// the limit allows, however many arrive at once. The same holds for a code's
/// single use: the commit claims the TOTP time step the code matched, so a code
/// presented again — on this challenge or on another — finds its step taken.
/// </remarks>
public class VerifyTwoFactorLoginCommandHandler : IRequestHandler<VerifyTwoFactorLoginCommand, ErrorOr<LoginResponse>>
{
    private readonly ITwoFactorChallengeRepository _challengeRepository;
    private readonly ISecondFactorVerifier _secondFactorVerifier;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly TotpReplayPolicy _replayPolicy;
    private readonly IUserRepository _userRepository;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<VerifyTwoFactorLoginCommandHandler> _logger;

    public VerifyTwoFactorLoginCommandHandler(
        ITwoFactorChallengeRepository challengeRepository,
        ISecondFactorVerifier secondFactorVerifier,
        ITwoFactorStateStore twoFactorStateStore,
        TotpReplayPolicy replayPolicy,
        IUserRepository userRepository,
        ILoginAttemptRepository loginAttemptRepository,
        IRefreshTokenKeyService refreshTokenKeyService,
        ILoginResponseBuilder loginResponseBuilder,
        IDomainEventDispatcher eventDispatcher,
        ILogger<VerifyTwoFactorLoginCommandHandler> logger)
    {
        _challengeRepository = challengeRepository;
        _secondFactorVerifier = secondFactorVerifier;
        _twoFactorStateStore = twoFactorStateStore;
        _replayPolicy = replayPolicy;
        _userRepository = userRepository;
        _loginAttemptRepository = loginAttemptRepository;
        _refreshTokenKeyService = refreshTokenKeyService;
        _loginResponseBuilder = loginResponseBuilder;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginResponse>> Handle(
        VerifyTwoFactorLoginCommand request,
        CancellationToken cancellationToken)
    {
        // Locate the pending challenge by the hash of the presented token.
        // Not-found, expired, used, and attempts-exhausted all map to the same
        // opaque error so the endpoint is not an oracle. This read is only the
        // fast path for a dead challenge: the reservation below decides.
        var tokenHash = _refreshTokenKeyService.ComputeTokenHash(request.ChallengeToken);
        var challenge = await _challengeRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (challenge == null || !challenge.IsValid)
        {
            return TwoFactorErrors.ChallengeInvalid;
        }

        var user = await _userRepository.GetByIdAsync(challenge.UserId, cancellationToken);
        if (user == null)
        {
            return TwoFactorErrors.ChallengeInvalid;
        }

        // Account state may have changed within the challenge window.
        var statusCheck = AuthenticationHelper.CheckAccountStatus(user);
        if (statusCheck.IsError)
        {
            return statusCheck.Errors;
        }

        if (user.IsLockedOut())
        {
            return UserErrors.AccountLockedUntil(user.LockoutEnd);
        }

        // The account first. A locked factor is refused here, before any attempt
        // is reserved on the challenge, and every request let through has already
        // been counted as a failure — a correct code is what clears it.
        var reservation = await _secondFactorVerifier.ReserveAsync(
            challenge.UserId, expectEnabled: true, cancellationToken);

        if (reservation.IsError)
        {
            // A factor switched off reads as a dead challenge, as it always has.
            return reservation.FirstError.Code == UserErrors.TwoFactorNotEnabled.Code
                ? TwoFactorErrors.ChallengeInvalid
                : reservation.Errors;
        }

        // Then the challenge. Its allowance, its expiry and single use are
        // conditions of the statement that counts this attempt, so a request the
        // read above let through still checks nothing once the challenge is spent.
        var attempt = await _challengeRepository.TryReserveAttemptAsync(
            challenge.Id, TwoFactorChallenge.MaxAttempts, cancellationToken);

        if (attempt == null)
        {
            return TwoFactorErrors.ChallengeInvalid;
        }

        var method = request.UseRecoveryCode ? SecondFactorMethod.RecoveryCode : SecondFactorMethod.Totp;
        var proof = await _secondFactorVerifier.VerifyAsync(
            reservation.Value, request.Code, method, cancellationToken);

        if (proof.IsError)
        {
            await EndCeremonyIfLastAttemptAsync(attempt.Value, challenge.Id, cancellationToken);

            _logger.LogWarning(
                "Failed two-factor verification for user {UserId} from {IpAddress}",
                user.Id, request.IpAddress);

            return proof.Errors;
        }

        // Single-use: consume the challenge and settle the factor in one
        // transaction before issuing tokens. A concurrent request that consumed
        // the challenge first, or spent the same recovery code on another one,
        // leaves this one with nothing — before the success is recorded and
        // before any token is minted. A TOTP code also claims its time step
        // there, so the same code presented again is refused.
        var commit = await _twoFactorStateStore.TryCommitLoginAsync(
            challenge.Id, user.Id, proof.Value, _replayPolicy.RejectReusedCodes, cancellationToken);

        if (commit == LoginCommitOutcome.StepReused)
        {
            // A correct code whose step was already accepted. Nothing was written:
            // the challenge stays open for the next code, and both reservations
            // stand — a reuse costs an attempt like any rejected code, so repeated
            // reuse ends the challenge and locks the factor. Logged, because a
            // reuse the user did not make means someone else saw the code and
            // holds the password. The code itself is never logged.
            await EndCeremonyIfLastAttemptAsync(attempt.Value, challenge.Id, cancellationToken);

            _logger.LogWarning(
                "Reused two-factor code rejected for user {UserId} from {IpAddress} on {Surface}",
                user.Id, request.IpAddress, "sign-in");

            return TwoFactorErrors.CodeAlreadyUsed;
        }

        if (commit == LoginCommitOutcome.ReuseAccepted)
        {
            // TwoFactor:RejectReusedCodes is off. The factor was settled, so this
            // is a success; the line is what an operator reviews while the switch
            // stays off.
            _logger.LogWarning(
                "Reused two-factor code accepted (RejectReusedCodes=false) for user {UserId} from {IpAddress} on {Surface}",
                user.Id, request.IpAddress, "sign-in");
        }
        else if (commit != LoginCommitOutcome.Committed)
        {
            // A correct code that still did not commit means another request won
            // the challenge, or spent the same recovery code, at the same instant
            // — worth a line, because concurrent correct codes on one account can
            // be a sign of a stolen code. The code itself is never logged.
            _logger.LogWarning(
                "Two-factor login commit lost ({Outcome}) for user {UserId} from {IpAddress} via {Method}",
                commit, user.Id, request.IpAddress, proof.Value.Method);
            return TwoFactorErrors.ChallengeInvalid;
        }

        // Record successful login on entity (raises UserLoggedInEvent)
        user.RecordSuccessfulLogin(request.IpAddress, request.UserAgent);

        // The challenge id travels with the build so the success — or a late
        // session-limit refusal — settles the row this ceremony already owns
        // instead of appending a second one.
        var loginResponse = await _loginResponseBuilder.BuildAsync(
            user, request.IpAddress, request.UserAgent, request.DeviceId, cancellationToken,
            twoFactorChallengeId: challenge.Id);

        if (loginResponse.IsError)
        {
            // At the concurrent session limit. The challenge has already been
            // consumed above and stays consumed — the code was correct, and
            // replaying it must not become a way around the limit.
            return loginResponse.Errors;
        }

        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);

        _logger.LogInformation(
            "Two-factor login completed for user {UserId} from {IpAddress}",
            user.Id, request.IpAddress);

        return loginResponse;
    }

    /// <summary>
    /// A refused code does not end the ceremony, so it does not write a row of its
    /// own — the count is kept on the challenge and surfaces in the history
    /// alongside the one row this sign-in owns. Only the attempt that spends the
    /// last of the allowance ends it, and that is the outcome worth recording,
    /// whether the code was wrong or already used.
    /// </summary>
    /// <param name="attempt">The challenge's attempt count, this attempt included.</param>
    private async Task EndCeremonyIfLastAttemptAsync(int attempt, Guid challengeId, CancellationToken cancellationToken)
    {
        if (attempt >= TwoFactorChallenge.MaxAttempts)
        {
            await _loginAttemptRepository.ResolveTwoFactorCeremonyAsync(
                challengeId, false, "Too many incorrect verification codes", cancellationToken);
        }
    }
}
