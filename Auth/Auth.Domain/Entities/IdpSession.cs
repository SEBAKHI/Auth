using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents the browser's single-sign-on session with the identity provider
/// itself — the server-side counterpart of the IdP session cookie. One IdP
/// session can spawn many per-application <see cref="UserSession"/>s through
/// authorization-code exchanges. Only the HMAC-SHA256 hash of the cookie token
/// is stored, never the plain token.
/// </summary>
public class IdpSession : EntityBase
{
    /// <summary>
    /// Gets the ID of the user this session belongs to.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the HMAC-SHA256 hash of the session cookie token.
    /// </summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when the session was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the session expires (absolute lifetime).
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the session was revoked (e.g. logout).
    /// </summary>
    public DateTime? RevokedAt { get; private set; }

    /// <summary>
    /// Gets the IP address the session was created from.
    /// </summary>
    public string? IpAddress { get; private set; }

    /// <summary>
    /// Gets the device/user-agent information the session was created with.
    /// </summary>
    public string? DeviceInfo { get; private set; }

    /// <summary>
    /// Gets whether the session has been revoked.
    /// </summary>
    public bool IsRevoked => RevokedAt.HasValue;

    private IdpSession() : base()
    {
    }

    public IdpSession(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTime createdAt,
        DateTime expiresAt,
        DateTime? revokedAt,
        string? ipAddress,
        string? deviceInfo) : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        RevokedAt = revokedAt;
        IpAddress = ipAddress;
        DeviceInfo = deviceInfo;
    }

    public static IdpSession Create(
        Guid userId,
        string tokenHash,
        TimeSpan lifetime,
        string? ipAddress,
        string? deviceInfo)
    {
        return new IdpSession
        {
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(lifetime),
            IpAddress = ipAddress,
            DeviceInfo = deviceInfo
        };
    }

    /// <summary>
    /// Checks if the session has expired.
    /// </summary>
    public bool IsExpired()
    {
        return DateTime.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// Checks if the session is valid (not expired and not revoked).
    /// </summary>
    public bool IsValid()
    {
        return !IsRevoked && !IsExpired();
    }

    /// <summary>
    /// Revokes the session (logout or security action).
    /// </summary>
    public void Revoke()
    {
        RevokedAt = DateTime.UtcNow;
    }
}
