using System.Collections.Concurrent;
using Auth.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// In-memory implementation of token blacklist.
/// Stores revoked token JTIs and user-level revocation timestamps.
/// Automatically cleans up expired entries.
/// </summary>
public class TokenBlacklistService : ITokenBlacklistService, IDisposable
{
    private readonly ConcurrentDictionary<string, DateTime> _blacklistedTokens = new();
    private readonly ConcurrentDictionary<string, DateTime> _blacklistedSessions = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _userRevocationTimes = new();
    private readonly ILogger<TokenBlacklistService> _logger;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public TokenBlacklistService(ILogger<TokenBlacklistService> logger)
    {
        _logger = logger;
        // Run cleanup every 5 minutes
        _cleanupTimer = new Timer(_ => CleanupExpiredEntries(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <inheritdoc />
    public void BlacklistToken(string jti, DateTime expiresAt)
    {
        if (string.IsNullOrEmpty(jti))
        {
            return;
        }

        _blacklistedTokens.TryAdd(jti, expiresAt);
        _logger.LogDebug("Blacklisted token with JTI: {Jti}, expires at: {ExpiresAt}", jti, expiresAt);
    }

    /// <inheritdoc />
    public void BlacklistAllUserTokens(Guid userId, DateTime revokedAt)
    {
        _userRevocationTimes.AddOrUpdate(userId, revokedAt, (_, existing) =>
            revokedAt > existing ? revokedAt : existing);

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
        _logger.LogDebug("Blacklisted session {SessionId}, expires at: {ExpiresAt}", sessionId, expiresAt);
    }

    /// <inheritdoc />
    public bool IsTokenBlacklisted(string jti)
    {
        if (string.IsNullOrEmpty(jti))
        {
            return false;
        }

        return _blacklistedTokens.ContainsKey(jti);
    }

    /// <inheritdoc />
    public bool IsSessionBlacklisted(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return false;
        }

        return _blacklistedSessions.ContainsKey(sessionId);
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

    /// <inheritdoc />
    public void CleanupExpiredEntries()
    {
        var now = DateTime.UtcNow;
        var expiredCount = 0;

        // Remove expired individual tokens
        foreach (var kvp in _blacklistedTokens)
        {
            if (kvp.Value < now)
            {
                _blacklistedTokens.TryRemove(kvp.Key, out _);
                expiredCount++;
            }
        }

        // Remove expired session blacklist entries
        foreach (var kvp in _blacklistedSessions)
        {
            if (kvp.Value < now)
            {
                _blacklistedSessions.TryRemove(kvp.Key, out _);
                expiredCount++;
            }
        }

        // Clean up old user revocation entries (older than 1 hour - access tokens should be expired by then)
        var cutoff = now.AddHours(-1);
        foreach (var kvp in _userRevocationTimes)
        {
            if (kvp.Value < cutoff)
            {
                _userRevocationTimes.TryRemove(kvp.Key, out _);
            }
        }

        if (expiredCount > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired blacklist entries", expiredCount);
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
