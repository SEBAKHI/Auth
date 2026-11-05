using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth.Application.Features.Passkeys.Common;

/// <summary>
/// What a password reset, a password change and an administrator's lock do to the
/// account's passkeys (S20, D-T19 D, ARR-0667): every passkey registered in the 72 hours
/// before it is revoked, one event each; the older ones stay, and the password notice lists
/// them so the owner can spot one they do not recognise.
/// </summary>
/// <remarks>
/// <para>
/// The threat is S20's own: a real-time phishing proxy that captured a session proving two
/// factors could register the attacker's passkey within the reauthentication window. The
/// owner's reset, change or the administrator's lock is the moment the account is taken
/// back, and a passkey that young may be the planted door.
/// </para>
/// <para>
/// The revocation runs after the change it follows has committed, with no token a client
/// can cancel, and never fails the request: the password is already changed, or the lock
/// already set. A failure is logged at Error (<c>passkey.revocation-failed</c>) for the
/// operator, and the passkeys stay as they were.
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
    /// The account's live passkeys of the current RP ID that a change cut at
    /// <paramref name="cutoff"/> keeps: those registered at or before it.
    /// </summary>
    public async Task<IReadOnlyList<PasskeySummary>> SurvivorsAsync(
        Guid userId, DateTime cutoff, CancellationToken cancellationToken)
    {
        var live = await _passkeyRepository.GetLiveAsync(userId, _feature.RpId, cancellationToken);
        return live
            .Where(passkey => passkey.CreatedAt <= cutoff)
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
