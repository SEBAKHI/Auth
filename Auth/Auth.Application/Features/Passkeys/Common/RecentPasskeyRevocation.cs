using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth.Application.Features.Passkeys.Common;

/// <summary>
/// What a password reset, a password change and an administrator's lock do to the
/// account's passkeys (S20, D-T19 D, ARR-0667): every passkey registered in the 72 hours
/// before it is revoked, one event each, and each one's owner is told which and why; the
/// older ones stay, and the password notice lists them so the owner can spot one they do
/// not recognise.
/// </summary>
/// <remarks>
/// <para>
/// The threat is S20's own: a real-time phishing proxy that captured a session proving two
/// factors could register the attacker's passkey within the reauthentication window. The
/// owner's reset, change or the administrator's lock is the moment the account is taken
/// back, and a passkey that young may be the planted door.
/// </para>
/// <para>
/// The caller runs it right after the change it follows has committed and the account's
/// sessions were ended, before any step that takes the request's token (REVIEW 150 F1,
/// F2): a client that disconnects cannot skip it, and a registration in flight either
/// committed before it, and is revoked here, or finds its session ended and stores nothing.
/// It takes no token a client can cancel, and never fails the request: the password is
/// already changed, or the lock already set. A failure is logged at Error
/// (<c>passkey.revocation-failed</c>) for the operator, and the passkeys stay as they were.
/// </para>
/// <para>
/// The notice's list is read after it (<see cref="StillLiveAsync"/>): every live passkey of
/// the RP ID in force, so when the revocation failed the recent ones are listed too, and
/// the notice never hides a passkey that still signs in.
/// </para>
/// </remarks>
public sealed class RecentPasskeyRevocation
{
    private readonly PasskeyFeature _feature;
    private readonly IUserPasskeyRepository _passkeyRepository;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecentPasskeyRevocation> _logger;

    public RecentPasskeyRevocation(
        PasskeyFeature feature,
        IUserPasskeyRepository passkeyRepository,
        IPublisher publisher,
        TimeProvider timeProvider,
        ILogger<RecentPasskeyRevocation> logger)
    {
        _feature = feature;
        _passkeyRepository = passkeyRepository;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// The moment from which a passkey is "recent" for a change made now: registered after it
    /// means revoked.
    /// </summary>
    public DateTime CutoffNow() =>
        _timeProvider.GetUtcNow().UtcDateTime - PasskeyRemovalReasons.RecentRegistrationWindow;

    /// <summary>
    /// The account's live passkeys of the current RP ID, read after
    /// <see cref="RevokeRecentAsync"/>: what the password notice lists. After a revocation
    /// that worked these are the ones registered before the cut; after one that failed, the
    /// recent ones too, so the owner sees every passkey that still signs in.
    /// </summary>
    public async Task<IReadOnlyList<PasskeySummary>> StillLiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var live = await _passkeyRepository.GetLiveAsync(userId, _feature.RpId, cancellationToken);
        return live
            .Select(passkey => new PasskeySummary(passkey.Name, passkey.CreatedAt, passkey.LastUsedAt))
            .ToList();
    }

    /// <summary>
    /// Revokes the account's live passkeys registered after <paramref name="cutoff"/>, of any
    /// RP ID, and raises one <see cref="PasskeyRemovedEvent"/> each. Never throws.
    /// </summary>
    /// <param name="user">The account, for the events.</param>
    /// <param name="cutoff">The cut, from <see cref="CutoffNow"/> when the change began.</param>
    /// <param name="reason"><see cref="PasskeyRemovalReasons.PasswordReset"/>, <see cref="PasskeyRemovalReasons.PasswordChange"/> or <see cref="PasskeyRemovalReasons.AdminLock"/>.</param>
    /// <param name="revokedBy">Who made the change: the owner, or the administrator.</param>
    /// <returns>How many were revoked; 0 when the revocation failed.</returns>
    public async Task<int> RevokeRecentAsync(User user, DateTime cutoff, string reason, Guid revokedBy)
    {
        IReadOnlyList<UserPasskey> revoked;
        try
        {
            revoked = await _passkeyRepository.RevokeRegisteredAfterAsync(
                user.Id, cutoff, reason, revokedBy, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "{EventCode}: the passkeys of user {UserId} registered after {Cutoff:O} could not be revoked ({Reason}); they stay as they were",
                PasskeySignalCodes.RevocationFailed, user.Id, cutoff, reason);
            return 0;
        }

        foreach (var passkey in revoked)
        {
            _logger.LogInformation(
                "{EventCode}: passkey {PasskeyId} of user {UserId} revoked ({Reason}): registered in the 72 hours before",
                PasskeySignalCodes.Removed, passkey.Id, user.Id, reason);

            await _publisher.Publish(
                new PasskeyRemovedEvent(
                    user.Id,
                    passkey.Id,
                    passkey.Name,
                    passkey.CreatedAt,
                    reason,
                    revokedBy,
                    user.Email.Value,
                    user.NameToShow,
                    DeviceName: null,
                    passkey.RevokedAt ?? _timeProvider.GetUtcNow().UtcDateTime),
                CancellationToken.None);
        }

        return revoked.Count;
    }
}
