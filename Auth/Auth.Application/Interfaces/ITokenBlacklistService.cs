namespace Auth.Application.Interfaces;

/// <summary>
/// Service for managing blacklisted (revoked) access tokens.
/// Used to immediately invalidate access tokens on logout before their natural expiration.
/// </summary>
public interface ITokenBlacklistService
{
    /// <summary>
    /// Blacklist a token by its JWT ID (jti claim).
    /// </summary>
    /// <param name="jti">The JWT ID to blacklist.</param>
    /// <param name="expiresAt">When the token naturally expires (for cleanup purposes).</param>
    void BlacklistToken(string jti, DateTime expiresAt);

    /// <summary>
    /// Blacklist all tokens for a specific user (logout all devices).
    /// Tokens issued before this timestamp will be rejected.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="revokedAt">The timestamp after which all tokens should be rejected.</param>
    void BlacklistAllUserTokens(Guid userId, DateTime revokedAt);

    /// <summary>
    /// Blacklist an entire login session by its session id (sid claim), so every
    /// access token carrying that sid is rejected until it would have expired.
    /// </summary>
    /// <param name="sessionId">The session id (sid claim) to blacklist.</param>
    /// <param name="expiresAt">When the blacklist entry can be cleaned up.</param>
    void BlacklistSession(string sessionId, DateTime expiresAt);

    /// <summary>
    /// Check if a specific token is blacklisted.
    /// </summary>
    /// <param name="jti">The JWT ID to check.</param>
    /// <returns>True if the token is blacklisted.</returns>
    bool IsTokenBlacklisted(string jti);

    /// <summary>
    /// Check if a login session has been blacklisted.
    /// </summary>
    /// <param name="sessionId">The session id (sid claim) to check.</param>
    /// <returns>True if the session is blacklisted.</returns>
    bool IsSessionBlacklisted(string sessionId);

    /// <summary>
    /// Check if all tokens for a user issued before a certain time are blacklisted.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="issuedAt">When the token was issued.</param>
    /// <returns>True if tokens issued at this time are blacklisted.</returns>
    bool AreUserTokensBlacklisted(Guid userId, DateTime issuedAt);

    /// <summary>
    /// Remove expired entries from the blacklist (cleanup).
    /// </summary>
    void CleanupExpiredEntries();
}
