using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Auth.Domain.ValueObjects;

namespace Auth.Application.Features.Authentication.VerifyTwoFactorLogin;

/// <summary>
/// Handler that completes a two-factor login: validates the pending challenge,
/// reserves the attempt on the account and then on the challenge, verifies the
/// TOTP code, recovery code or emailed code, and only then issues tokens via the
/// shared login response builder.
/// </summary>
/// <remarks>
/// Every limit here is enforced by a conditional write, not by what a read saw.
/// A burst of concurrent guesses all read the same counts, so a check on a read
/// lets every one of them through; a reservation lets through exactly as many as
/// the limit allows, however many arrive at once. The same holds for a code's
/// single use: the commit claims the TOTP time step the code matched, so a code
/// presented again — on this challenge or on another — finds its step taken.
/// <para>
/// The emailed code (S24) goes the same way on its own counter: an earlier code this
/// challenge emailed is refused before anything is counted (only the mailbox holder
/// could present it), and the commit consumes the challenge, so the code signs in
/// once. What it proves is recorded and never counted as a second factor.
/// </para>
/// </remarks>
public class VerifyTwoFactorLoginCommandHandler : IRequestHandler<VerifyTwoFactorLoginCommand, ErrorOr<LoginResponse>>
{
    private readonly ITwoFactorChallengeRepository _challengeRepository;
    private readonly ISecondFactorVerifier _secondFactorVerifier;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly TotpReplayPolicy _replayPolicy;
    private readonly IUserRepository _userRepository;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly ICredentialHasher _credentialHasher;
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
        ICredentialHasher credentialHasher,
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
        _credentialHasher = credentialHasher;
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
        var candidates = _credentialHasher.Candidates(CredentialPurpose.TwoFactorChallenge, request.ChallengeToken);
        var challenge = await _challengeRepository.GetByTokenHashAsync(candidates, cancellationToken);

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

        // An administrator's lock refuses the second step as it refuses renewal.
        // An automatic lock (timed or hard) does not: the challenge was issued
        // after the first factor was proved, by a familiar source or before the
        // lock, and the step is bounded by the challenge's own five attempts and
        // five minutes (owner decision RV-P6-6). Completing it lifts the lock only
        // when that first factor was the password.
        // Under the hard lock the step completes only a PASSWORD sign-in: a
        // provider sign-in is refused under it (LK3.2), so a challenge a provider
        // opened before the limit was reached does not complete after it.
        var lockKind = user.LockKind(DateTime.UtcNow);
        if (lockKind == AccountLockKind.Admin
            || (lockKind == AccountLockKind.Hard && !challenge.PrimaryMethod.ProvesPassword))
        {
            return user.LockRefusal(lockKind);
        }

        // The account first, on the counter of the factor the code is presented as.
        // A locked factor is refused here, before any attempt is reserved on the
        // challenge, and every request let through has already been counted as a
        // failure — a correct code is what clears it. A factor the account does not
        // hold, an emailed code a newer one replaced, or an emailed code after a
        // first factor other than the password is refused before anything is counted.
        // The validator refused a method that names none; a request that reached here
        // without it gets the same answer, never an exception.
        if (request.PresentedMethod is not { } method)
        {
            return TwoFactorErrors.MethodInvalid;
        }

        var reservation = await _secondFactorVerifier.ReserveAsync(
            new SecondFactorPresentation(challenge.UserId, method, request.Code, challenge), cancellationToken);

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

            _logger.ReusedCodeRejected(user.Id, TotpReplayLog.SignIn, request.IpAddress);

            return TwoFactorErrors.CodeAlreadyUsed;
        }

        if (commit == LoginCommitOutcome.ReuseAccepted)
        {
            // TwoFactor:RejectReusedCodes is off. The factor was settled, so this
            // is a success; the line is what an operator reviews while the switch
            // stays off.
            _logger.ReusedCodeAccepted(user.Id, TotpReplayLog.SignIn, request.IpAddress);
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

        // The whole sign-in: the first factor that opened the challenge and the
        // second one just settled. A challenge issued before first factors were
        // recorded contributes nothing, so the session has no first factor on
        // record — and a platform administrator is asked to sign in again. An
        // emailed code is recorded as such, and a recovery code issued without two
        // factors as nothing (FA3): neither makes the session a two-factor one.
        var methods = challenge.PrimaryMethod.With(proof.Value.Methods);

        // The challenge id travels with the build so the success — or a late
        // session-limit refusal — settles the row this ceremony already owns
        // instead of appending a second one.
        // Only a challenge opened by the password proves it; one opened by a
        // provider, an emailed code, or recorded before first factors were
        // (Unknown) lifts nothing. The second step does not change that: an emailed
        // code completing a password sign-in lifts the lock as the authenticator
        // app's would (owner decision G8), because the password was proved.
        var loginResponse = await _loginResponseBuilder.BuildAsync(
            user, request.IpAddress, request.UserAgent, request.DeviceId, methods,
            passwordProven: challenge.PrimaryMethod.ProvesPassword, cancellationToken,
            twoFactorChallengeId: challenge.Id, persistent: request.StaySignedIn);

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
