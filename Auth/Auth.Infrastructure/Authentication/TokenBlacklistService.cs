using System.Collections.Concurrent;
using System.Threading.Channels;
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
public class TokenBlacklistService : ITokenBlacklistService, IDisposable
{
    private readonly ConcurrentDictionary<string, DateTime> _blacklistedTokens = new();
    private readonly ConcurrentDictionary<string, DateTime> _blacklistedSessions = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _userRevocationTimes = new();
    private readonly ChannelWriter<TokenRevocation> _persistenceQueue;
    private readonly ILogger<TokenBlacklistService> _logger;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    // Bounds how long a User-level (logout-all) entry lives beyond the revocation
    // instant — comfortably longer than any access-token lifetime.
    private static readonly TimeSpan UserRevocationRetention = TimeSpan.FromHours(1);

    public TokenBlacklistService(
        ChannelWriter<TokenRevocation> persistenceQueue,
        ILogger<TokenBlacklistService> logger)
    {
        _persistenceQueue = persistenceQueue;
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

        _blacklistedTokens.TryAdd(jti, expiresAt);
        Persist(new TokenRevocation(RevocationType.Token, jti, DateTime.UtcNow, expiresAt));
        _logger.LogDebug("Blacklisted token with JTI: {Jti}, expires at: {ExpiresAt}", jti, expiresAt);
    }

    /// <inheritdoc />
    public void BlacklistAllUserTokens(Guid userId, DateTime revokedAt)
    {
        _userRevocationTimes.AddOrUpdate(userId, revokedAt, (_, existing) =>
            revokedAt > existing ? revokedAt : existing);

        Persist(new TokenRevocation(
            RevocationType.User, userId.ToString(), revokedAt, revokedAt.Add(UserRevocationRetention)));
        _logger.LogInformation("Blacklisted all tokens for user {UserId} issued before {RevokedAt}", userId, revokedAt);
    }

    /// <inheritdoc />
    public void BlacklistSession(string sessionId, DateTime expiresAt)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        _blacklistedSessions.TryAdd(sessionId, expiresAt);
        Persist(new TokenRevocation(RevocationType.Session, sessionId, DateTime.UtcNow, expiresAt));
        _logger.LogDebug("Blacklisted session {SessionId}, expires at: {ExpiresAt}", sessionId, expiresAt);
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
        if (_userRevocationTimes.TryGetValue(userId, out var revocationTime))
        {
            return issuedAt <= revocationTime;
        }

        return false;
    }

    /// <summary>
    /// Repopulates the in-memory cache from durable storage (called on startup).
    /// Expired entries are ignored.
    /// </summary>
    public void LoadSnapshot(IEnumerable<TokenRevocation> revocations)
    {
        var now = DateTime.UtcNow;
        var loaded = 0;

        foreach (var revocation in revocations)
        {
            if (revocation.ExpiresAt <= now)
            {
                continue;
            }

            switch (revocation.Type)
            {
                case RevocationType.Token:
                    _blacklistedTokens.TryAdd(revocation.Key, revocation.ExpiresAt);
                    break;
                case RevocationType.Session:
                    _blacklistedSessions.TryAdd(revocation.Key, revocation.ExpiresAt);
                    break;
                case RevocationType.User when Guid.TryParse(revocation.Key, out var userId):
                    _userRevocationTimes.AddOrUpdate(userId, revocation.EffectiveAt, (_, existing) =>
                        revocation.EffectiveAt > existing ? revocation.EffectiveAt : existing);
                    break;
            }

            loaded++;
        }

        if (loaded > 0)
        {
            _logger.LogInformation("Rehydrated {Count} token revocations from durable store", loaded);
        }
    }

    /// <inheritdoc />
    public void CleanupExpiredEntries()
    {
        var now = DateTime.UtcNow;

        foreach (var kvp in _blacklistedTokens)
        {
            if (kvp.Value < now)
            {
                _blacklistedTokens.TryRemove(kvp.Key, out _);
            }
        }

        foreach (var kvp in _blacklistedSessions)
        {
            if (kvp.Value < now)
            {
                _blacklistedSessions.TryRemove(kvp.Key, out _);
            }
        }

        var cutoff = now.Subtract(UserRevocationRetention);
        foreach (var kvp in _userRevocationTimes)
        {
            if (kvp.Value < cutoff)
            {
                _userRevocationTimes.TryRemove(kvp.Key, out _);
            }
        }
    }

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
