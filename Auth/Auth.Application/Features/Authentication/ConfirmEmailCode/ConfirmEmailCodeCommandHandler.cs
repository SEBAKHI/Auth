using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.ConfirmEmailCode;

/// <summary>
/// Handler for the command that binds the emailed code as the second factor (S24).
/// </summary>
/// <remarks>
/// <para>
/// The code proves the mailbox, so it is the only emailed code asked for: the code an
/// account binding its first factor otherwise also presents (X02,
/// <see cref="FirstFactorEmailProofPolicy"/>) would prove the same thing twice. A
/// recent sign-in first (OI-220 (3)), and email still on.
/// </para>
/// <para>
/// The code is checked against the enrollment challenge alone, under its five
/// attempts; a wrong, superseded or expired code gets the one answer binding has
/// always given (<c>TwoFactor.EmailCodeInvalid</c>), so a guesser learns nothing. Then
/// one transaction spends the challenge and writes the factor row from the state it
/// was read in: a new row, a pending authenticator app replaced, or an enabled one
/// replaced (D-T34 B) — for which the rules of <see cref="AuthenticatorReplacementRule"/>
/// are applied again, since the app may have been bound after the code was asked for.
/// </para>
/// <para>
/// Recovery codes are issued only when the row has none, and those do not count as a
/// second factor (FA3): the session may have proved the password alone. Codes the row
/// already held — issued with the app it replaces — stay as they were.
/// </para>
/// <para>
/// Replacing an authenticator app signs every other session out, as switching it off
/// does; a first bind does not. The owner is told by email, once.
/// </para>
/// </remarks>
public class ConfirmEmailCodeCommandHandler
    : IRequestHandler<ConfirmEmailCodeCommand, ErrorOr<EmailCodeConfirmedResponse>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly IPlatformMfaPolicy _platformMfaPolicy;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ITwoFactorChallengeRepository _challengeRepository;
    private readonly IUserRepository _userRepository;
    private readonly IOtpHasher _otpHasher;
    private readonly ITotpService _totpService;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<ConfirmEmailCodeCommandHandler> _logger;

    public ConfirmEmailCodeCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        IPlatformMfaPolicy platformMfaPolicy,
        ITwoFactorStateStore twoFactorStateStore,
        ITwoFactorChallengeRepository challengeRepository,
        IUserRepository userRepository,
        IOtpHasher otpHasher,
        ITotpService totpService,
        ICredentialRevocationService credentialRevocation,
        IDomainEventDispatcher eventDispatcher,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<ConfirmEmailCodeCommandHandler> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _platformMfaPolicy = platformMfaPolicy;
        _twoFactorStateStore = twoFactorStateStore;
        _challengeRepository = challengeRepository;
        _userRepository = userRepository;
        _otpHasher = otpHasher;
        _totpService = totpService;
        _credentialRevocation = credentialRevocation;
        _eventDispatcher = eventDispatcher;
        // A snapshot, read in this request's scope: Email:Enabled is hot.
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<EmailCodeConfirmedResponse>> Handle(
        ConfirmEmailCodeCommand request,
        CancellationToken cancellationToken)
    {
        // 1. A recent sign-in, before anything is read, counted or written.
        var session = await _reauthenticationGuard.EnsureRecentSignInAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        // 2. Email off since the code was asked for: no sign-in could send one.
        if (!_emailSettings.Enabled)
        {
            return TwoFactorErrors.EmailCodeUnavailable;
        }

        // 3. The enrollment the code was emailed for. None live: nothing can match,
        //    so nothing is counted.
        var challenge = await _challengeRepository.GetLiveForUserAsync(
            request.UserId, TwoFactorChallengePurpose.EmailEnrollment, cancellationToken);
        if (challenge?.EmailCodeHash is null)
        {
            return TwoFactorErrors.EmailCodeInvalid;
        }

        // 4. The factor as it is now, and the rules that depend on it — before any
        //    attempt is counted.
        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(request.UserId, cancellationToken);
        var allowed = await EnsureMayBindAsync(request.UserId, snapshot, session.Value, cancellationToken);
        if (allowed.IsError)
        {
            return allowed.Errors;
        }

        // 5. One of the challenge's five attempts, then the code.
        if (await _challengeRepository.TryReserveAttemptAsync(
                challenge.Id, TwoFactorChallengePurpose.EmailEnrollment, TwoFactorChallenge.MaxAttempts, cancellationToken) is null
            || !EmailCodeProofStrategy.IsLiveCode(_otpHasher, challenge, request.Code))
        {
            _logger.LogWarning(
                "A wrong or expired code was presented to bind the emailed second factor for user {UserId}",
                request.UserId);
            return TwoFactorErrors.EmailCodeInvalid;
        }

        // Read before the commit, with the request's token: once the factor is bound,
        // nothing that follows may be stopped by the caller going away.
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // 6. The challenge and the factor row, in one transaction. A row that changed
        //    between the read and the write — a setup in another tab rotating a pending
        //    secret — leaves the code unspent, so the bind is tried once more against
        //    the row as it is then.
        var (outcome, recoveryCodes) = await BindAsync(request.UserId, challenge, snapshot, cancellationToken);
        if (outcome == LoginCommitOutcome.FactorLost)
        {
            snapshot = await _twoFactorStateStore.GetSnapshotAsync(request.UserId, cancellationToken);
            allowed = await EnsureMayBindAsync(request.UserId, snapshot, session.Value, cancellationToken);
            if (allowed.IsError)
            {
                return allowed.Errors;
            }

            (outcome, recoveryCodes) = await BindAsync(request.UserId, challenge, snapshot, cancellationToken);
        }

        switch (outcome)
        {
            case LoginCommitOutcome.Committed:
                break;

            case LoginCommitOutcome.ChallengeLost:
                // A newer code was sent, or another request spent this one, first.
                return TwoFactorErrors.EmailCodeInvalid;

            default:
                // The row changed again under the second try: another request is
                // binding a factor at the same moment, and it won.
                return UserErrors.TwoFactorAlreadyEnabled;
        }

        // 7. Recorded on the aggregate only now, for a change that happened.
        var replacedAuthenticator = snapshot?.IsEnabled == true;
        user.EnableEmailCodeFactor(request.UserId, session.Value.DeviceName, replacedAuthenticator);

        _logger.LogInformation(
            "The emailed code became the second factor of user {UserId}{Replaced}",
            request.UserId, replacedAuthenticator ? ", replacing the authenticator app" : string.Empty);

        // 8. Replacing the app signs every other session and browser out, as switching
        //    it off does; a first bind does not. Neither this nor the notice may be
        //    undone by a client that disconnects, and a failed revocation must not cost
        //    the owner the email or the request its success.
        if (replacedAuthenticator)
        {
            await _credentialRevocation.RevokeOtherSessionsAsync(
                request.UserId, request.CurrentSessionId, request.IdpSessionToken,
                "Authenticator app replaced by the emailed code", _logger);
        }

        // 9. The audit rows and the email to the owner.
        await _eventDispatcher.DispatchEventsAsync(user, CancellationToken.None);

        return new EmailCodeConfirmedResponse(recoveryCodes);
    }

    private async Task<ErrorOr<Success>> EnsureMayBindAsync(
        Guid userId,
        TwoFactorSnapshot? snapshot,
        RecentSession session,
        CancellationToken cancellationToken)
    {
        if (snapshot?.HasEmailCode == true)
        {
            return UserErrors.TwoFactorAlreadyEnabled;
        }

        return snapshot?.IsEnabled == true
            ? await AuthenticatorReplacementRule.EnsureAllowedAsync(_platformMfaPolicy, userId, session, cancellationToken)
            : Result.Success;
    }

    /// <returns>
    /// The store's outcome, and the new recovery codes — null when the row's codes stay.
    /// </returns>
    private async Task<(LoginCommitOutcome Outcome, string[]? RecoveryCodes)> BindAsync(
        Guid userId,
        TwoFactorChallenge challenge,
        TwoFactorSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        // Generated and hashed before the transaction: no hashing inside it.
        string[]? recoveryCodes = null;
        string? recoveryCodesJson = null;
        // Issued when none is left — the column NULL, or the set the last spent code
        // emptied — so the emailed code never stands without a way in apart from the
        // mailbox.
        if (snapshot?.HasRecoveryCodes != true)
        {
            (recoveryCodes, recoveryCodesJson) = _totpService.IssueRecoveryCodes();
        }

        var outcome = await _twoFactorStateStore.TryBindEmailCodeAsync(
            new EmailCodeBinding(userId, challenge.Id, challenge.EmailCodeHash!, snapshot, recoveryCodesJson),
            cancellationToken);

        return (outcome, recoveryCodes);
    }
}
