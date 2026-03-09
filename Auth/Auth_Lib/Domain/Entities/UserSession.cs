using Auth_Lib.Domain.Primitives;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents an active user session for tracking and management.
/// </summary>
public class UserSession : EntityBase
{
    /// <summary>
    /// Gets the ID of the user this session belongs to.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the ID of the application this session is for.
    /// </summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>
    /// Gets the ID of the associated refresh token.
    /// </summary>
    public Guid? RefreshTokenId { get; private set; }

    /// <summary>
    /// Gets the unique session token hash.
    /// </summary>
    public string SessionTokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the IP address from which the session was created.
    /// </summary>
    public string? IpAddress { get; private set; }

    /// <summary>
    /// Gets the user agent string.
    /// </summary>
    public string? UserAgent { get; private set; }

    /// <summary>
    /// Gets the device identifier.
    /// </summary>
    public string? DeviceId { get; private set; }

    /// <summary>
    /// Gets the device name/description.
    /// </summary>
    public string? DeviceName { get; private set; }

    /// <summary>
    /// Gets the approximate location based on IP.
    /// </summary>
    public string? Location { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the session was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the session expires.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp of the last activity.
    /// </summary>
    public DateTime LastActivityAt { get; private set; }

    /// <summary>
    /// Gets whether the session is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the session was terminated.
    /// </summary>
    public DateTime? TerminatedAt { get; private set; }

    /// <summary>
    /// Gets the reason for session termination.
    /// </summary>
    public string? TerminationReason { get; private set; }

    private UserSession() : base()
    {
    }

    public UserSession(
        Guid id,
        Guid userId,
        Guid applicationId,
        Guid? refreshTokenId,
        string sessionTokenHash,
        string? ipAddress,
        string? userAgent,
        string? deviceId,
        string? deviceName,
        string? location,
        DateTime createdAt,
        DateTime expiresAt,
        DateTime lastActivityAt,
        bool isActive,
        DateTime? terminatedAt,
        string? terminationReason) : base(id)
    {
        UserId = userId;
        ApplicationId = applicationId;
        RefreshTokenId = refreshTokenId;
        SessionTokenHash = sessionTokenHash;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        DeviceId = deviceId;
        DeviceName = deviceName;
        Location = location;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        LastActivityAt = lastActivityAt;
        IsActive = isActive;
        TerminatedAt = terminatedAt;
        TerminationReason = terminationReason;
    }

    public static UserSession Create(
        Guid userId,
        Guid applicationId,
        Guid? refreshTokenId,
        string sessionTokenHash,
        TimeSpan lifetime,
        string? ipAddress,
        string? userAgent,
        string? deviceId = null,
        string? deviceName = null,
        string? location = null)
    {
        var now = DateTime.UtcNow;
        return new UserSession
        {
            UserId = userId,
            ApplicationId = applicationId,
            RefreshTokenId = refreshTokenId,
            SessionTokenHash = sessionTokenHash,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceId = deviceId,
            DeviceName = deviceName,
            Location = location,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
            LastActivityAt = now,
            IsActive = true
        };
    }

    /// <summary>
    /// Updates the last activity timestamp.
    /// </summary>
    public void RecordActivity()
    {
        LastActivityAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Extends the session expiration.
    /// </summary>
    public void Extend(TimeSpan duration)
    {
        ExpiresAt = DateTime.UtcNow.Add(duration);
        LastActivityAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Terminates the session.
    /// </summary>
    public void Terminate(string reason)
    {
        IsActive = false;
        TerminatedAt = DateTime.UtcNow;
        TerminationReason = reason;
    }

    /// <summary>
    /// Checks if the session is valid (active and not expired).
    /// </summary>
    public bool IsValid()
    {
        return IsActive && !IsExpired();
    }

    /// <summary>
    /// Checks if the session has expired.
    /// </summary>
    public bool IsExpired()
    {
        return DateTime.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// Associates a refresh token with this session.
    /// </summary>
    public void SetRefreshToken(Guid refreshTokenId)
    {
        RefreshTokenId = refreshTokenId;
    }
}
