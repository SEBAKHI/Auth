using System.Text.Json;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using Auth.Domain.ValueObjects;
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
/// <para>
/// Binding the account's FIRST factor while email is on also needs the code
/// emailed to its confirmed address (<see cref="FirstFactorEmailProofPolicy"/>), so
/// whoever holds only the password cannot bind an authenticator of their own. It is
/// checked after the authenticator code, under the same counted attempt — a wrong
/// email code is a failure of the pending factor, which locks at five — and spent
/// in the same transaction that switches the factor on. It is never a factor.
/// </para>
/// <para>
/// The session the code is entered in is upgraded in the same transaction: the
/// authenticator code is a second factor proved inside it, so its next refresh
/// counts it — the way an administrator who enrols from the two-step page gets the
/// platform authority back. The emailed code is not a factor and upgrades nothing.
/// </para>
/// <para>
/// An account that already holds a second factor may bind another only from a
/// session that proved two (FA11 (11-2)): otherwise whoever holds the password
/// could add an authenticator of their own next to the owner's.
/// </para>
/// </remarks>
public class EnableTwoFactorCommandHandler : IRequestHandler<EnableTwoFactorCommand, ErrorOr<EnableTwoFactorResponse>>
{
    private const int RecoveryCodeCount = 10;

    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly ISecondFactorVerifier _secondFactorVerifier;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ITotpService _totpService;
    private readonly TotpReplayPolicy _replayPolicy;
    private readonly FirstFactorEmailProofPolicy _emailProofPolicy;
    private readonly FirstFactorEmailProof _emailProof;
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<EnableTwoFactorCommandHandler> _logger;

    public EnableTwoFactorCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        ISecondFactorVerifier secondFactorVerifier,
        ITwoFactorStateStore twoFactorStateStore,
        ITotpService totpService,
        TotpReplayPolicy replayPolicy,
        FirstFactorEmailProofPolicy emailProofPolicy,
        FirstFactorEmailProof emailProof,
        IUserRepository userRepository,
        IRefreshTokenKeyService refreshTokenKeyService,
        IDomainEventDispatcher eventDispatcher,
        ILogger<EnableTwoFactorCommandHandler> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _secondFactorVerifier = secondFactorVerifier;
        _twoFactorStateStore = twoFactorStateStore;
        _totpService = totpService;
        _replayPolicy = replayPolicy;
        _emailProofPolicy = emailProofPolicy;
        _emailProof = emailProof;
        _userRepository = userRepository;
        _refreshTokenKeyService = refreshTokenKeyService;
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

        // A further factor needs a session that proved two (FA11 (11-2)), before
        // anything is counted. With an authenticator on the account the reservation
        // below refuses anyway; the rule stands for every factor that can coexist
        // with one, and it is why the emailed code is never asked for here.
        if (!session.Value.Methods.IsMfaSatisfied
            && await _twoFactorStateStore.HasEnabledFactorAsync(request.UserId, cancellationToken))
        {
            _logger.LogInformation(
                "Binding a further second factor for user {UserId} asked for a two-factor sign-in first",
                request.UserId);
            return AuthErrors.ReauthenticationRequired;
        }

        // Read once: both settings are hot, and one request decides once.
        var emailProofRequired = _emailProofPolicy.IsRequired;
        var emailCode = string.IsNullOrEmpty(request.EmailCode) ? null : request.EmailCode;

        if (emailProofRequired && emailCode is null)
        {
            // A first factor being set up, and no emailed code: the request can never
            // succeed, so it costs no attempt. A client built before the step existed
            // lands here too. Without a pending row the reservation below answers —
            // set up first, or the factor is on already and no email step applies.
            var snapshot = await _twoFactorStateStore.GetSnapshotAsync(request.UserId, cancellationToken);
            if (snapshot is { IsEnabled: false })
            {
                return TwoFactorErrors.EmailCodeRequired;
            }
        }

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

        // The emailed code, checked under the attempt the factor already counted: a
        // wrong one leaves that reservation standing, so guessing it locks the
        // pending factor after five tries — and setup again does not clear the lock.
        // Its own attempt is counted too. No hashing waits for the transaction.
        Guid? bindCodeId = null;
        if (emailProofRequired)
        {
            if (emailCode is null)
            {
                // A pending factor appeared after the read above.
                return TwoFactorErrors.EmailCodeRequired;
            }

            var bindCode = await _emailProof.ReserveAsync(request.UserId, emailCode, cancellationToken);
            if (bindCode.IsError)
            {
                _logger.LogWarning(
                    "Invalid email code during 2FA enable for user {UserId}",
                    request.UserId);
                return bindCode.Errors;
            }

            bindCodeId = bindCode.Value;
        }

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

        // The session the code was entered in, and its SSO session when the cookie
        // arrived, gain the authenticator code — never the emailed code, which is
        // not a factor.
        var sessionUpgrade = new SessionUpgrade(
            session.Value.SessionId,
            string.IsNullOrEmpty(request.IdpSessionToken)
                ? null
                : _refreshTokenKeyService.ComputeTokenHash(request.IdpSessionToken),
            AuthenticationMethods.Totp);

        // The emailed code first, when the bind needed one; then the factor row —
        // only while it still holds the secret the code was checked against — its
        // codes, the code's step and the account flag, then the session upgrade, in
        // one transaction.
        var outcome = await _twoFactorStateStore.TryEnableAsync(
            request.UserId,
            reservation.Value.Snapshot.ProtectedSecretKey,
            recoveryCodesJson,
            step,
            _replayPolicy.RejectReusedCodes,
            bindCodeId,
            sessionUpgrade,
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

            case LoginCommitOutcome.ChallengeLost:
                // A concurrent request spent the emailed code first. Nothing was
                // written, and the codes generated here are never shown.
                return TwoFactorErrors.EmailCodeInvalid;

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
