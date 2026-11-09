using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.Reauthenticate;

/// <summary>
/// Handler that lets a signed-in person prove themself again with a password or an emailed code,
/// with no new sign-in (owner decision, 2026-11-09): the same proof a sign-in asks for, in a small
/// window where the person stands, so a change to a security setting no longer signs them out of
/// everything.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The session first, with nothing counted: a token with no live session row can only sign in
/// again.</item>
/// <item>An administrator whose only usable factor is a passkey is refused (D-S20-4 (ii-s)), and so
/// is any proof this account does not take (<see cref="ReauthenticationRequirement"/>), uncounted:
/// there is nothing to guess.</item>
/// <item>The first proof: the password through the door of the password sign-in
/// (<see cref="PasswordDoor"/>), so its guesses are counted and bounded the same; or the emailed
/// code, of an account without a password, under its challenge's five attempts.</item>
/// <item>The second step a sign-in would ask for, counted before it is checked
/// (<see cref="ISecondFactorVerifier"/>).</item>
/// <item>One transaction: what the proof spends, the session stamped with what this one proof
/// proved, and the session and its SSO session raised by it.</item>
/// <item>The audit row follows the commit. No email: nothing about the account changed.</item>
/// </list>
/// A proof lifts no lock and clears no count of wrong passwords: nothing signed in.
/// </remarks>
public sealed class ReauthenticateCommandHandler : IRequestHandler<ReauthenticateCommand, ErrorOr<Success>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly IUserRepository _userRepository;
    private readonly ReauthenticationPolicy _policy;
    private readonly PasskeyOnlyAdministratorRule _passkeyOnlyAdministrator;
    private readonly PasswordDoor _passwordDoor;
    private readonly ITwoFactorChallengeRepository _challengeRepository;
    private readonly IOtpHasher _otpHasher;
    private readonly ISecondFactorVerifier _secondFactorVerifier;
    private readonly EmailCodeFactorProof _emailCodeFactorProof;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ICredentialHasher _credentialHasher;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReauthenticateCommandHandler> _logger;

    public ReauthenticateCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        IUserRepository userRepository,
        ReauthenticationPolicy policy,
        PasskeyOnlyAdministratorRule passkeyOnlyAdministrator,
        PasswordDoor passwordDoor,
        ITwoFactorChallengeRepository challengeRepository,
        IOtpHasher otpHasher,
        ISecondFactorVerifier secondFactorVerifier,
        EmailCodeFactorProof emailCodeFactorProof,
        ITwoFactorStateStore twoFactorStateStore,
        ICredentialHasher credentialHasher,
        IPublisher publisher,
        TimeProvider timeProvider,
        ILogger<ReauthenticateCommandHandler> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _userRepository = userRepository;
        _policy = policy;
        _passkeyOnlyAdministrator = passkeyOnlyAdministrator;
        _passwordDoor = passwordDoor;
        _challengeRepository = challengeRepository;
        _otpHasher = otpHasher;
        _secondFactorVerifier = secondFactorVerifier;
        _emailCodeFactorProof = emailCodeFactorProof;
        _twoFactorStateStore = twoFactorStateStore;
        _credentialHasher = credentialHasher;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(ReauthenticateCommand request, CancellationToken cancellationToken)
    {
        // 1. The session, before anything is counted.
        var session = await _reauthenticationGuard.EnsureLiveSessionAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return AuthErrors.ReauthenticationRequired;
        }

        // 2. D-S20-4 (ii-s): such an administrator proves itself with its passkey alone. Asked
        //    before any proof, unlike a sign-in, because the caller is signed in as that account
        //    already: there is nobody to hide the answer from, and nothing to count.
        if (await _passkeyOnlyAdministrator.RefusalAsync(
                user.Id, PasskeyOnlyAdministratorDoors.Reauthentication, cancellationToken) is { } passkeyRequired)
        {
            return passkeyRequired;
        }

        var requirement = await _policy.ForAsync(user, cancellationToken);

        // 3. The first proof.
        var first = string.IsNullOrEmpty(request.Password)
            ? await ProveEmailCodeAsync(user, requirement, request, cancellationToken)
            : await ProvePasswordAsync(user, requirement, request, cancellationToken);
        if (first.IsError)
        {
            return first.Errors;
        }

        // 4. The second step.
        var second = await ProveSecondStepAsync(user.Id, requirement, request, cancellationToken);
        if (second.IsError)
        {
            return second.Errors;
        }

        // 5. All of it, in one transaction.
        var methods = second.Value.Proof is { } proof ? first.Value.Methods.With(proof.Methods) : first.Value.Methods;
        var upgrade = new SessionUpgrade(
            session.Value.Id,
            string.IsNullOrEmpty(request.IdpSessionToken)
                ? null
                : _credentialHasher.Candidates(CredentialPurpose.IdpSession, request.IdpSessionToken),
            methods);

        var outcome = await _twoFactorStateStore.TryCommitSessionProofAsync(
            user.Id, new SessionProofCommit(upgrade, first.Value.Code, second.Value.Proof), cancellationToken);

        switch (outcome)
        {
            case LoginCommitOutcome.Committed:
                break;

            case LoginCommitOutcome.StepReused:
                // Refused like a wrong code, and counted like one: the reservation stays.
                _logger.ReusedCodeRejected(user.Id, TotpReplayLog.StepUp, request.IpAddress);
                return TwoFactorErrors.CodeAlreadyUsed;

            case LoginCommitOutcome.RecoveryCodesChanged:
                // A concurrent sign-in spent a code between the check and the commit.
                return TwoFactorErrors.InvalidRecoveryCode;

            case LoginCommitOutcome.ChallengeLost:
                // A newer code was sent, or another request spent this one, first.
                return TwoFactorErrors.EmailCodeInvalid;

            case LoginCommitOutcome.SessionLost:
                return AuthErrors.ReauthenticationRequired;

            default:
                // The factor was switched off or removed meanwhile — or an outcome this handler
                // does not know, which never stamps anything.
                return UserErrors.TwoFactorNotEnabled;
        }

        _logger.LogInformation(
            "User {UserId} proved themself again in session {SessionId} (methods {AuthMethods})",
            user.Id, session.Value.Id, methods.Value);

        // The audit row. The session is already stamped, so a client that disconnects must not
        // cancel it.
        await _publisher.Publish(
            new UserReauthenticatedEvent(user.Id, session.Value.Id, methods), CancellationToken.None);

        return Result.Success;
    }

    /// <summary>The password, through the password sign-in's own door.</summary>
    private async Task<ErrorOr<FirstProof>> ProvePasswordAsync(
        User user,
        ReauthenticationRequirement requirement,
        ReauthenticateCommand request,
        CancellationToken cancellationToken)
    {
        // No password, or an address nobody proved: the password here proves nothing. Nothing is
        // hashed or counted.
        if (!requirement.Password)
        {
            return UserErrors.InvalidCurrentPassword;
        }

        var accepted = await _passwordDoor.CheckAsync(
            user,
            new PasswordAttempt(user.Email.Value, request.Password!, request.IpAddress, request.UserAgent, request.DeviceId),
            cancellationToken);

        if (accepted.IsError)
        {
            // The person is signed in: the field's own sentence, not the sign-in's.
            return accepted.FirstError.Code == UserErrors.InvalidCredentials.Code
                ? UserErrors.InvalidCurrentPassword
                : accepted.Errors;
        }

        return new FirstProof(AuthenticationMethods.Password, Code: null);
    }

    /// <summary>The emailed code of an account without a password: its first factor (D-215-1).</summary>
    private async Task<ErrorOr<FirstProof>> ProveEmailCodeAsync(
        User user,
        ReauthenticationRequirement requirement,
        ReauthenticateCommand request,
        CancellationToken cancellationToken)
    {
        // An account this code proves nothing for: its mailbox is not its first factor. Nothing is
        // counted.
        if (!requirement.EmailCode)
        {
            return TwoFactorErrors.EmailCodeInvalid;
        }

        var statusCheck = AuthenticationHelper.CheckAccountStatus(user);
        if (statusCheck.IsError)
        {
            return statusCheck.Errors;
        }

        // Such an account has no password to guess, so only an administrator's lock can stand on
        // it. An emailed code proves the mailbox, never the password, and lifts nothing (G8).
        var lockKind = user.LockKind(_timeProvider.GetUtcNow().UtcDateTime);
        if (lockKind is not (AccountLockKind.None or AccountLockKind.Lapsed))
        {
            return user.LockRefusal(lockKind);
        }

        // Expired, superseded, spent and never sent are one answer, as at binding the emailed code.
        var challenge = await _challengeRepository.GetLiveForUserAsync(
            user.Id, TwoFactorChallengePurpose.Reauthentication, cancellationToken);
        if (challenge?.EmailCodeHash is null)
        {
            return TwoFactorErrors.EmailCodeInvalid;
        }

        if (await _challengeRepository.TryReserveAttemptAsync(
                challenge.Id, TwoFactorChallengePurpose.Reauthentication, TwoFactorChallenge.MaxAttempts, cancellationToken) is null
            || !EmailCodeProofStrategy.IsLiveCode(_otpHasher, challenge, request.EmailCode!))
        {
            _logger.LogWarning(
                "A wrong or expired code was presented by user {UserId} proving themself again", user.Id);
            return TwoFactorErrors.EmailCodeInvalid;
        }

        return new FirstProof(
            AuthenticationMethods.ForEmailCodeSignIn(accountHasPassword: false),
            new EmailedCodeSpend(challenge.Id, challenge.EmailCodeHash));
    }

    /// <summary>
    /// The second step a sign-in of this account would ask for, counted first; null when it would
    /// ask for none.
    /// </summary>
    private async Task<ErrorOr<SecondStepProof>> ProveSecondStepAsync(
        Guid userId,
        ReauthenticationRequirement requirement,
        ReauthenticateCommand request,
        CancellationToken cancellationToken)
    {
        if (requirement.SecondStep == ReauthenticationSecondStep.None)
        {
            return new SecondStepProof(null);
        }

        // Nothing to check, so nothing is counted: a form that left it empty.
        if (string.IsNullOrEmpty(request.SecondStepCode))
        {
            return UserErrors.InvalidTwoFactorCode;
        }

        var proof = requirement.SecondStep == ReauthenticationSecondStep.AuthenticatorApp
            ? await ProveAuthenticatorAsync(userId, request.SecondStepCode, request.UseRecoveryCode, cancellationToken)
            : await _emailCodeFactorProof.VerifyAsync(
                userId,
                request.SecondStepCode,
                // With email off a recovery code is the only way, whatever the form said.
                request.UseRecoveryCode || requirement.SecondStep == ReauthenticationSecondStep.RecoveryCodeOnly,
                TwoFactorChallengePurpose.Reauthentication,
                cancellationToken);

        if (proof.IsError)
        {
            _logger.LogWarning(
                "A wrong second-step code was presented by user {UserId} proving themself again", userId);
            return proof.Errors;
        }

        return new SecondStepProof(proof.Value);
    }

    private async Task<ErrorOr<SecondFactorProof>> ProveAuthenticatorAsync(
        Guid userId,
        string code,
        bool isRecoveryCode,
        CancellationToken cancellationToken)
    {
        var method = isRecoveryCode ? SecondFactorMethod.RecoveryCode : SecondFactorMethod.Totp;

        var reservation = await _secondFactorVerifier.ReserveAsync(
            new SecondFactorPresentation(userId, method, code, Challenge: null), cancellationToken);
        if (reservation.IsError)
        {
            return reservation.Errors;
        }

        return await _secondFactorVerifier.VerifyAsync(reservation.Value, code, method, cancellationToken);
    }

    /// <param name="Methods">What the first proof proved.</param>
    /// <param name="Code">The emailed code to spend with the commit; null for a password.</param>
    private sealed record FirstProof(AuthenticationMethods Methods, EmailedCodeSpend? Code);

    /// <param name="Proof">What the second step proved; null when the account has none.</param>
    private sealed record SecondStepProof(SecondFactorProof? Proof);
}
