using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

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
    /// Gets the HMAC-SHA256 hash of the token for secure lookups.
    /// The plain token is never stored in the database.
    /// </summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the JWT ID (jti claim) of the associated access token.
    /// </summary>
    public string JwtId { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the ID of the login session this token belongs to. Stable across
    /// token rotation so a session can be tracked and revoked as a unit.
    /// </summary>
    public Guid? SessionId { get; private set; }

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
    /// Gets the hash of the token that replaced this one (for token rotation tracking).
    /// </summary>
    public string? ReplacedByTokenHash { get; private set; }

    /// <summary>
    /// Gets the reason for revocation.
    /// </summary>
    public string? ReasonRevoked { get; private set; }

    /// <summary>
    /// Gets whether the token has been revoked.
    /// </summary>
    public bool IsRevoked => RevokedAt.HasValue;

    /// <summary>
    /// Gets whether a deliberate server-side revocation ended this token — a
    /// reuse cascade, "sign out everywhere", a lockout, or account deletion —
    /// rather than an ordinary rotation superseding it.
    ///
    /// The distinction decides whether presenting the token again is evidence
    /// of theft. A ROTATED token was spent by whoever held it, so a second
    /// presentation means two parties hold it. A token killed in bulk was never
    /// spent by its holder at all: seeing it again is the account owner's other
    /// device discovering that its session was ended elsewhere. Treating that
    /// as a fresh attack is what turned a single incident into a
    /// self-perpetuating cascade, because each innocent device triggered
    /// another mass revocation that killed whatever session the user had just
    /// signed back in to.
    ///
    /// An unknown or missing reason counts as a rotation, deliberately: the
    /// conservative default is to keep detecting, never to fall silent.
    /// </summary>
    public bool WasTerminatedInBulk =>
        IsRevoked
        && !string.IsNullOrWhiteSpace(ReasonRevoked)
        && !string.Equals(ReasonRevoked, Constants.TokenRevocationReasons.Rotated, StringComparison.Ordinal);

    private RefreshToken() : base()
    {
    }

    public RefreshToken(
        Guid id,
        Guid userId,
        string tokenHash,
        string jwtId,
        Guid? sessionId,
        Guid? applicationId,
        string? deviceInfo,
        string? ipAddress,
        DateTime createdAt,
        DateTime expiresAt,
        DateTime? revokedAt,
        Guid? revokedBy,
        string? replacedByTokenHash,
        string? reasonRevoked) : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        JwtId = jwtId;
        SessionId = sessionId;
        ApplicationId = applicationId;
        DeviceInfo = deviceInfo;
        IpAddress = ipAddress;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        RevokedAt = revokedAt;
        RevokedBy = revokedBy;
        ReplacedByTokenHash = replacedByTokenHash;
        ReasonRevoked = reasonRevoked;
    }

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        string jwtId,
        Guid? applicationId,
        TimeSpan lifetime,
        string? ipAddress,
        string? deviceInfo,
        Guid? sessionId = null)
    {
        return new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            JwtId = jwtId,
            SessionId = sessionId,
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
    /// <param name="revokedBy">The ID of the user who revoked the token.</param>
    /// <param name="reason">The reason for revocation.</param>
    /// <param name="replacedByTokenHash">The hash of the new token that replaced this one (for rotation tracking).</param>
    public void Revoke(Guid? revokedBy, string reason, string? replacedByTokenHash = null)
    {
        RevokedAt = DateTime.UtcNow;
        RevokedBy = revokedBy;
        ReasonRevoked = reason;
        ReplacedByTokenHash = replacedByTokenHash;
    }

    /// <summary>
    /// Rotates the token by creating a new one and revoking this one.
    /// </summary>
    /// <param name="newTokenHash">The HMAC-SHA256 hash of the new token.</param>
    /// <param name="newJwtId">The JWT ID of the new associated access token.</param>
    /// <param name="lifetime">The lifetime of the new token.</param>
    /// <param name="ipAddress">The IP address from which the rotation was requested.</param>
    /// <param name="revokedBy">The ID of the user who initiated the rotation.</param>
    /// <returns>The new refresh token entity.</returns>
    public RefreshToken Rotate(
        string newTokenHash,
        string newJwtId,
        TimeSpan lifetime,
        string? ipAddress,
        Guid? revokedBy)
    {
        var newRefreshToken = Create(
            UserId,
            newTokenHash,
            newJwtId,
            ApplicationId,
            lifetime,
            ipAddress,
            DeviceInfo,
            SessionId);

        Revoke(revokedBy, Constants.TokenRevocationReasons.Rotated, newTokenHash);
        return newRefreshToken;
    }
}
