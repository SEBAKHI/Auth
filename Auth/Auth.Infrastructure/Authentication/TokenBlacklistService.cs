using System.Collections.Concurrent;
using System.Threading.Channels;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Token blacklist backed by an in-memory read cache and a durable store.
///
/// Per-request checks (Is*Blacklisted) hit only the in-memory dictionaries, so
/// they stay allocation-free and DB-free on the hot path. Writes update memory
/// immediately AND enqueue the revocation to a channel; a background service
/// persists it to <c>RevokedTokens</c> and rehydrates memory from that table on
/// startup. This is what makes revocation survive app-pool recycles — otherwise
/// a logout would stop rejecting a still-valid access token after the next
/// recycle wiped the in-memory list.
/// </summary>
/// <remarks>
/// Every entry is kept at least as long as the tokens it covers can be accepted
/// (X04, RV2): a user-level entry until <see cref="RevocationHorizon.User"/> after
/// it took effect, a session entry no shorter than that from now, and a single
/// token's entry until its exp plus the largest clock skew.
/// </remarks>
public class TokenBlacklistService : ITokenBlacklistService, IDisposable
{
    private readonly ConcurrentDictionary<string, DateTime> _blacklistedTokens = new();
    private readonly ConcurrentDictionary<string, DateTime> _blacklistedSessions = new();
    private readonly ConcurrentDictionary<Guid, UserRevocation> _userRevocations = new();
    private readonly ChannelWriter<TokenRevocation> _persistenceQueue;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TokenBlacklistService> _logger;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    /// <summary>
    /// A user-level revocation: tokens issued at or before <see cref="EffectiveAt"/>
    /// are refused, and the entry is dropped at <see cref="ExpiresAt"/>.
    /// </summary>
    private readonly record struct UserRevocation(DateTime EffectiveAt, DateTime ExpiresAt)
    {
        public UserRevocation Merge(UserRevocation other) => new(
            other.EffectiveAt > EffectiveAt ? other.EffectiveAt : EffectiveAt,
            other.ExpiresAt > ExpiresAt ? other.ExpiresAt : ExpiresAt);
    }

    public TokenBlacklistService(
        ChannelWriter<TokenRevocation> persistenceQueue,
        TimeProvider timeProvider,
        ILogger<TokenBlacklistService> logger)
    {
        _persistenceQueue = persistenceQueue;
        _timeProvider = timeProvider;
        _logger = logger;
        // Run in-memory cleanup every 5 minutes (durable purge is the background service's job).
        _cleanupTimer = new Timer(_ => CleanupExpiredEntries(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Longest key this store accepts. Matches RevokedTokens.RevocationKey
    /// (NVARCHAR(200)), so nothing can sit in memory that the durable copy
    /// would refuse — and this process never issues a jti or session id
    /// anywhere near it (they are GUIDs). Anything longer is not one of ours.
    /// </summary>
    public const int MaxKeyLength = 200;

    /// <summary>
    /// When a user-level revocation that took effect at <paramref name="effectiveAt"/>
    /// can be dropped, whatever expiry its durable row carries: rows written before X04
    /// carry one hour, and still count until the horizon.
    /// </summary>
    private static DateTime UserRevocationExpiry(DateTime effectiveAt, DateTime storedExpiresAt)
    {
        var horizon = effectiveAt + RevocationHorizon.User;
        return storedExpiresAt > horizon ? storedExpiresAt : horizon;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    /// <inheritdoc />
    public void BlacklistToken(string jti, DateTime expiresAt)
    {
        if (string.IsNullOrEmpty(jti))
        {
            return;
        }

        if (jti.Length > MaxKeyLength)
        {
            _logger.LogWarning(
                "Refused to blacklist a {Length}-character jti: keys longer than {Max} are never issued by this process and cannot be persisted",
                jti.Length, MaxKeyLength);
            return;
        }

        // Validation accepts a token until its exp plus the clock skew, so the entry
        // lasts that long too: the largest skew, whatever is configured now.
        var keptUntil = expiresAt + JwtSettings.MaxClockSkew;
        _blacklistedTokens.AddOrUpdate(jti, keptUntil, (_, existing) => Later(existing, keptUntil));
        Persist(new TokenRevocation(RevocationType.Token, jti, UtcNow, keptUntil));
        _logger.LogDebug("Blacklisted token with JTI: {Jti}, kept until: {KeptUntil}", jti, keptUntil);
    }

    /// <inheritdoc />
    public void BlacklistAllUserTokens(Guid userId, DateTime revokedAt)
    {
        var revocation = new UserRevocation(revokedAt, revokedAt + RevocationHorizon.User);
        _userRevocations.AddOrUpdate(userId, revocation, (_, existing) => existing.Merge(revocation));

        Persist(new TokenRevocation(
            RevocationType.User, userId.ToString(), revocation.EffectiveAt, revocation.ExpiresAt));
        _logger.LogInformation("Blacklisted all tokens for user {UserId} issued before {RevokedAt}", userId, revokedAt);
    }

    /// <inheritdoc />
    public void BlacklistSession(string sessionId, DateTime expiresAt)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        // The caller's value is a floor, never the answer: a session row's expiry or the
        // lifetime in force now can both fall short of a token issued under a longer
        // lifetime. No token issued before now outlives now + the horizon.
        var keptUntil = Later(expiresAt, UtcNow + RevocationHorizon.User);
        _blacklistedSessions.AddOrUpdate(sessionId, keptUntil, (_, existing) => Later(existing, keptUntil));
        Persist(new TokenRevocation(RevocationType.Session, sessionId, UtcNow, keptUntil));
        _logger.LogDebug("Blacklisted session {SessionId}, kept until: {KeptUntil}", sessionId, keptUntil);
    }

    /// <inheritdoc />
    public bool IsTokenBlacklisted(string jti)
    {
        return !string.IsNullOrEmpty(jti) && _blacklistedTokens.ContainsKey(jti);
    }

    /// <inheritdoc />
    public bool IsSessionBlacklisted(string sessionId)
    {
        return !string.IsNullOrEmpty(sessionId) && _blacklistedSessions.ContainsKey(sessionId);
    }

    /// <inheritdoc />
    public bool AreUserTokensBlacklisted(Guid userId, DateTime issuedAt)
    {
        if (_userRevocations.TryGetValue(userId, out var revocation))
        {
            return issuedAt <= revocation.EffectiveAt;
        }

        return false;
    }

    /// <summary>
    /// Merges durable revocations into the in-memory cache: at startup, and on every
    /// periodic re-read. An entry is only ever added or extended, never shortened or
    /// removed, so a re-read cannot undo a revocation this process made itself.
    /// Entries already past their expiry are ignored.
    /// </summary>
    /// <returns>How many revocations of the snapshot are still in force.</returns>
    public int LoadSnapshot(IEnumerable<TokenRevocation> revocations)
    {
        var now = UtcNow;
        var loaded = 0;

        foreach (var revocation in revocations)
        {
            switch (revocation.Type)
            {
                case RevocationType.Token when revocation.ExpiresAt > now:
                    _blacklistedTokens.AddOrUpdate(
                        revocation.Key, revocation.ExpiresAt, (_, existing) => Later(existing, revocation.ExpiresAt));
                    break;
                case RevocationType.Session when revocation.ExpiresAt > now:
                    _blacklistedSessions.AddOrUpdate(
                        revocation.Key, revocation.ExpiresAt, (_, existing) => Later(existing, revocation.ExpiresAt));
                    break;
                case RevocationType.User when Guid.TryParse(revocation.Key, out var userId):
                    var user = new UserRevocation(
                        revocation.EffectiveAt, UserRevocationExpiry(revocation.EffectiveAt, revocation.ExpiresAt));
                    if (user.ExpiresAt <= now)
                    {
                        continue;
                    }

                    _userRevocations.AddOrUpdate(userId, user, (_, existing) => existing.Merge(user));
                    break;
                default:
                    continue;
            }

            loaded++;
        }

        return loaded;
    }

    /// <inheritdoc />
    public void CleanupExpiredEntries()
    {
        var now = UtcNow;

        // Removal by key AND value: an entry extended between the read and the removal
        // stays.
        foreach (var kvp in _blacklistedTokens)
        {
            if (kvp.Value < now)
            {
                _blacklistedTokens.TryRemove(kvp);
            }
        }

        foreach (var kvp in _blacklistedSessions)
        {
            if (kvp.Value < now)
            {
                _blacklistedSessions.TryRemove(kvp);
            }
        }

        foreach (var kvp in _userRevocations)
        {
            if (kvp.Value.ExpiresAt < now)
            {
                _userRevocations.TryRemove(kvp);
            }
        }
    }

    private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

    /// <summary>
    /// Enqueues a revocation for durable persistence. Non-blocking; the
    /// in-memory effect is already applied, so a dropped enqueue only loses
    /// durability of that one entry (the session's refresh token is revoked in
    /// the DB regardless), never the immediate in-memory revocation.
    /// </summary>
    private void Persist(TokenRevocation revocation)
    {
        if (!_persistenceQueue.TryWrite(revocation))
        {
            _logger.LogWarning(
                "Could not enqueue revocation {Type}:{Key} for durable persistence", revocation.Type, revocation.Key);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cleanupTimer.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
