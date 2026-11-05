using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.RemovePasskey;

/// <summary>
/// Handler that removes one of the signed-in account's passkeys (S20, PK12).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The proof: a recent sign-in that proved a first factor, and — when the account has
/// the authenticator app — two factors. Never an assertion of the passkey being removed: a
/// lost passkey cannot give one, and asking for it would trap the person who lost it.</item>
/// <item>Only a live passkey of the caller, of the current RP ID: anything else is
/// <c>Passkey.NotFound</c>, with no event and no revocation.</item>
/// <item>The last qualifying factor of an administrator the policy binds stays:
/// <c>TwoFactor.RequiredByPolicy</c>, as for switching the app off. The statement that
/// revokes it keeps the floor, so two removals at once cannot take the last two.</item>
/// <item>The row is revoked in one statement; then every other session and SSO session of
/// the account is ended through the revocation service, the caller's own kept (a failure
/// there is logged and the removal stands); then the event (the audit row and the owner's
/// notice).</item>
/// </list>
/// </remarks>
public sealed class RemovePasskeyCommandHandler : IRequestHandler<RemovePasskeyCommand, ErrorOr<Success>>
{
    private const string RevocationReason = "Passkey removed";

    private readonly PasskeyFeature _feature;
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly IPlatformMfaPolicy _platformMfaPolicy;
    private readonly IUserRepository _userRepository;
    private readonly IUserPasskeyRepository _passkeyRepository;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RemovePasskeyCommandHandler> _logger;

    public RemovePasskeyCommandHandler(
        PasskeyFeature feature,
        IReauthenticationGuard reauthenticationGuard,
        ITwoFactorStateStore twoFactorStateStore,
        IPlatformMfaPolicy platformMfaPolicy,
        IUserRepository userRepository,
        IUserPasskeyRepository passkeyRepository,
        ICredentialRevocationService credentialRevocation,
        IPublisher publisher,
        TimeProvider timeProvider,
        ILogger<RemovePasskeyCommandHandler> logger)
    {
        _feature = feature;
        _reauthenticationGuard = reauthenticationGuard;
        _twoFactorStateStore = twoFactorStateStore;
        _platformMfaPolicy = platformMfaPolicy;
        _userRepository = userRepository;
        _passkeyRepository = passkeyRepository;
        _credentialRevocation = credentialRevocation;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(RemovePasskeyCommand request, CancellationToken cancellationToken)
    {
        // 1. A recent sign-in that proved a first factor, and two when the account has the app.
        var session = await _reauthenticationGuard.EnsureRecentSignInAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        var methods = session.Value.Methods;
        var factors = await _twoFactorStateStore.GetQualifyingFactorsAsync(request.UserId, cancellationToken);
        if (!methods.HasPrimary || (factors.Totp && !methods.IsMfaSatisfied))
        {
            _logger.LogInformation(
                "Removing a passkey of user {UserId} asked for a stronger sign-in (methods {AuthMethods})",
                request.UserId, methods.Value);
            return AuthErrors.ReauthenticationRequired;
        }

        // 2. The caller's own live passkey of the current RP ID.
        var rpId = _feature.RpId;
        var passkey = await _passkeyRepository.GetLiveByIdAsync(request.UserId, request.PasskeyId, rpId, cancellationToken);
        if (passkey is null)
        {
            return PasskeyErrors.NotFound;
        }

        // 3. An administrator the policy binds keeps a qualifying factor: refused before
        //    anything changes, so asking costs nothing. The count here is a courtesy; the
        //    floor that holds when two removals race is in the revocation itself.
        var keepsAFactor = !factors.Totp
            && await _platformMfaPolicy.IsEnforcedForUserAsync(request.UserId, cancellationToken);
        if (keepsAFactor
            && await _passkeyRepository.CountLiveAsync(request.UserId, rpId, cancellationToken) <= 1)
        {
            return RefusedAsTheLastFactor(request.UserId);
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // 4. The row, then the other sessions, then the event. From the first write on, a
        //    client that disconnects must not stop it halfway. For a bound administrator the
        //    statement itself keeps one live passkey (REVIEW 150 F4): of two removals that
        //    would each take one of the last two, one changes nothing.
        var revoked = keepsAFactor
            ? await _passkeyRepository.TryRevokeUnlessLastAsync(
                request.UserId, request.PasskeyId, rpId, PasskeyRemovalReasons.User, request.UserId, CancellationToken.None)
            : await _passkeyRepository.TryRevokeAsync(
                request.UserId, request.PasskeyId, rpId, PasskeyRemovalReasons.User, request.UserId, CancellationToken.None);
        if (revoked is null)
        {
            // Nothing changed: the passkey was not the caller's live one, or the floor kept it.
            return keepsAFactor
                && await _passkeyRepository.GetLiveByIdAsync(request.UserId, request.PasskeyId, rpId, CancellationToken.None) is not null
                ? RefusedAsTheLastFactor(request.UserId)
                : PasskeyErrors.NotFound;
        }

        // A failed revocation must not cost the owner the audit row, the notice or the request
        // its success: the passkey is already revoked, and a retry would find it gone
        // (REVIEW 150 F3, as switching the app off does). It is logged for an operator, and
        // those sessions live on until they expire.
        var sessionsEnded = 0;
        try
        {
            sessionsEnded = await _credentialRevocation.RevokeCredentialsAsync(
                request.UserId,
                request.CurrentSessionId,
                request.IdpSessionToken,
                revokedBy: request.UserId,
                RevocationReason,
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Passkey {PasskeyId} of user {UserId} was removed, but the account's other sessions could not be signed out",
                revoked.Id, request.UserId);
        }

        _logger.LogInformation(
            "{EventCode}: passkey {PasskeyId} of user {UserId} removed by its owner; {SessionsEnded} other sessions ended",
            PasskeySignalCodes.Removed, revoked.Id, request.UserId, sessionsEnded);

        await _publisher.Publish(
            new PasskeyRemovedEvent(
                request.UserId,
                revoked.Id,
                revoked.Name,
                revoked.CreatedAt,
                PasskeyRemovalReasons.User,
                request.UserId,
                user.Email.Value,
                user.NameToShow,
                session.Value.DeviceName,
                revoked.RevokedAt ?? _timeProvider.GetUtcNow().UtcDateTime),
            CancellationToken.None);

        return Result.Success;
    }

    private Error RefusedAsTheLastFactor(Guid userId)
    {
        _logger.LogInformation(
            "Removing the last passkey of user {UserId} was refused: platform administrators must keep a second factor",
            userId);
        return TwoFactorErrors.RequiredByPolicy;
    }
}
