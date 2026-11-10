using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.ReauthenticateWithPasskey;

/// <summary>
/// Handler that lets a signed-in person prove themself again with a passkey, with no new sign-in
/// (owner decision, 2026-11-09).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The session first, with nothing spent: a token with no live session row can only sign in
/// again.</item>
/// <item>The steps every passkey sign-in takes (<see cref="PasskeyAssertionCeremony"/>): the
/// challenge spent, the credential, the user handle, the library's check, the counter.</item>
/// <item>The passkey must be this account's. A device may hold passkeys of several, and the options
/// name none, so this is what binds the answer to the session: another account's passkey proved
/// that account's owner, not this one.</item>
/// <item>The status and the lock, as at a passkey sign-in (<see cref="PasskeyLockAdmission"/>).</item>
/// <item>The session stamped with what the passkey proved, and raised by it, with its SSO
/// session.</item>
/// <item>The audit row. No email: nothing about the account changed.</item>
/// </list>
/// A revocation of this passkey that lands after the check ends the account's sessions, this one
/// included (<see cref="PasskeyAssertionCeremony.StillLiveAfterTheSessionAsync"/>'s order), so a
/// stamp can only outlive it on a session that is ending.
/// </remarks>
public sealed class ReauthenticateWithPasskeyCommandHandler
    : IRequestHandler<ReauthenticateWithPasskeyCommand, ErrorOr<Success>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly PasskeyAssertionCeremony _ceremony;
    private readonly IUserRepository _userRepository;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ICredentialHasher _credentialHasher;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReauthenticateWithPasskeyCommandHandler> _logger;

    public ReauthenticateWithPasskeyCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        PasskeyAssertionCeremony ceremony,
        IUserRepository userRepository,
        ITwoFactorStateStore twoFactorStateStore,
        ICredentialHasher credentialHasher,
        IPublisher publisher,
        TimeProvider timeProvider,
        ILogger<ReauthenticateWithPasskeyCommandHandler> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _ceremony = ceremony;
        _userRepository = userRepository;
        _twoFactorStateStore = twoFactorStateStore;
        _credentialHasher = credentialHasher;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        ReauthenticateWithPasskeyCommand request,
        CancellationToken cancellationToken)
    {
        // 1. The session, before anything is spent.
        var session = await _reauthenticationGuard.EnsureLiveSessionAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        // 2. The assertion, and 3. this account's passkey and no other: the ceremony refuses another
        //    account's before it records anything of it (Passkey.NotThisAccount).
        var proven = await _ceremony.ProveAsync(
            request.ChallengeHandle,
            request.ChallengeHandleRepeated,
            request.ResponseJson,
            cancellationToken,
            signedInAs: request.UserId);
        if (proven.IsError)
        {
            return proven.Errors;
        }

        // 4. The status and the lock, as at a passkey sign-in.
        var owner = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (owner is null)
        {
            return AuthErrors.ReauthenticationRequired;
        }

        var statusCheck = AuthenticationHelper.CheckAccountStatus(owner);
        if (statusCheck.IsError)
        {
            return statusCheck.Errors;
        }

        if (PasskeyLockAdmission.Judge(owner, _timeProvider.GetUtcNow().UtcDateTime, _logger) is { } lockRefusal)
        {
            return lockRefusal;
        }

        // 5. The session. Its challenge is spent: a client that disconnects now must not lose it.
        var methods = AuthenticationMethods.Passkey.With(AuthenticationMethods.PasskeyUserVerified);
        var upgrade = new SessionUpgrade(
            session.Value.Id,
            string.IsNullOrEmpty(request.IdpSessionToken)
                ? null
                : _credentialHasher.Candidates(CredentialPurpose.IdpSession, request.IdpSessionToken),
            methods);

        // Stamped by the API's clock, which the guard reads it and the access token's iat by.
        var outcome = await _twoFactorStateStore.TryCommitSessionProofAsync(
            request.UserId,
            new SessionProofCommit(upgrade, _timeProvider.GetUtcNow().UtcDateTime),
            CancellationToken.None);
        if (outcome != LoginCommitOutcome.Committed)
        {
            return AuthErrors.ReauthenticationRequired;
        }

        _logger.LogInformation(
            "User {UserId} proved themself again with passkey {PasskeyId} in session {SessionId}",
            request.UserId, proven.Value.Id, session.Value.Id);

        // 6. The audit row, with where the proof came from.
        await _publisher.Publish(
            new UserReauthenticatedEvent(request.UserId, session.Value.Id, methods, request.IpAddress, request.UserAgent),
            CancellationToken.None);

        return Result.Success;
    }
}
