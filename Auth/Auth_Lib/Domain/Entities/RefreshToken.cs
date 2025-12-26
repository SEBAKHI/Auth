using Auth_Lib.Foundation.Base;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents a refresh token for JWT token renewal.
/// Supports token rotation for enhanced security.
/// </summary>
public class RefreshToken : EntityBase
{
    /// <summary>
    /// Gets the ID of the user this token belongs to.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the actual token value.
    /// </summary>
    public string Token { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the SHA256 hash of the token value for secure lookups.
    /// </summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the JWT ID (jti claim) of the associated access token.
    /// </summary>
    public string JwtId { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the ID of the application this token belongs to.
    /// </summary>
    public Guid? ApplicationId { get; private set; }

    /// <summary>
    /// Gets the device/session information (user agent, device ID, etc.).
    /// </summary>
    public string? DeviceInfo { get; private set; }

    /// <summary>
    /// Gets the IP address from which the token was created.
    /// </summary>
    public string? IpAddress { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the token was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the token expires.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the token was revoked.
    /// </summary>
    public DateTime? RevokedAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who revoked the token.
    /// </summary>
    public Guid? RevokedBy { get; private set; }

    /// <summary>
    /// Gets the token that replaced this one (for token rotation tracking).
    /// </summary>
    public string? ReplacedByToken { get; private set; }

    /// <summary>
    /// Gets the reason for revocation.
    /// </summary>
    public string? ReasonRevoked { get; private set; }

    /// <summary>
    /// Gets whether the token has been revoked.
    /// </summary>
    public bool IsRevoked => RevokedAt.HasValue;

    private RefreshToken() : base()
    {
    }

    public RefreshToken(
        Guid id,
        Guid userId,
        string token,
        string tokenHash,
        string jwtId,
        Guid? applicationId,
        string? deviceInfo,
        string? ipAddress,
        DateTime createdAt,
        DateTime expiresAt,
        DateTime? revokedAt,
        Guid? revokedBy,
        string? replacedByToken,
        string? reasonRevoked) : base(id)
    {
        UserId = userId;
        Token = token;
        TokenHash = tokenHash;
        JwtId = jwtId;
        ApplicationId = applicationId;
        DeviceInfo = deviceInfo;
        IpAddress = ipAddress;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        RevokedAt = revokedAt;
        RevokedBy = revokedBy;
        ReplacedByToken = replacedByToken;
        ReasonRevoked = reasonRevoked;
    }

    public static RefreshToken Create(
        Guid userId,
        string token,
        string tokenHash,
        string jwtId,
        Guid? applicationId,
        TimeSpan lifetime,
        string? ipAddress,
        string? deviceInfo)
    {
        return new RefreshToken
        {
            UserId = userId,
            Token = token,
            TokenHash = tokenHash,
            JwtId = jwtId,
            ApplicationId = applicationId,
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(lifetime)
        };
    }

    /// <summary>
    /// Checks if the token is valid (not expired and not revoked).
    /// </summary>
    public bool IsValid()
    {
        return !IsRevoked && !IsExpired();
    }

    /// <summary>
    /// Checks if the token has expired.
    /// </summary>
    public bool IsExpired()
    {
        return DateTime.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// Revokes the token.
    /// </summary>
    public void Revoke(Guid? revokedBy, string reason, string? replacedByToken = null)
    {
        RevokedAt = DateTime.UtcNow;
        RevokedBy = revokedBy;
        ReasonRevoked = reason;
        ReplacedByToken = replacedByToken;
    }

    /// <summary>
    /// Rotates the token by creating a new one and revoking this one.
    /// </summary>
    public RefreshToken Rotate(
        string newToken,
        string newTokenHash,
        string newJwtId,
        TimeSpan lifetime,
        string? ipAddress,
        Guid? revokedBy)
    {
        var newRefreshToken = Create(
            UserId,
            newToken,
            newTokenHash,
            newJwtId,
            ApplicationId,
            lifetime,
            ipAddress,
            DeviceInfo);

        Revoke(revokedBy, "Rotated", newToken);
        return newRefreshToken;
    }
}
