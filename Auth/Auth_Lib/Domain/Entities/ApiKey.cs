using Auth_Lib.Foundation.Base;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents an API key for external system authentication.
/// </summary>
public class ApiKey : EntityBase
{
    /// <summary>
    /// Gets the ID of the application this API key belongs to.
    /// </summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>
    /// Gets the name of this API key.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the description of this API key.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the prefix of the key for identification (e.g., "ak_prod_").
    /// </summary>
    public string KeyPrefix { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the Argon2id hash of the full key.
    /// </summary>
    public string KeyHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the environment (production, staging, development).
    /// </summary>
    public string Environment { get; private set; } = "production";

    /// <summary>
    /// Gets the rate limit per minute.
    /// </summary>
    public int RateLimitPerMinute { get; private set; }

    /// <summary>
    /// Gets the rate limit per day.
    /// </summary>
    public int RateLimitPerDay { get; private set; }

    /// <summary>
    /// Gets the allowed IP addresses as JSON array.
    /// </summary>
    public string? AllowedIps { get; private set; }

    /// <summary>
    /// Gets the allowed CORS origins as JSON array.
    /// </summary>
    public string? AllowedOrigins { get; private set; }

    /// <summary>
    /// Gets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who created this key.
    /// </summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>
    /// Gets the optional expiration timestamp.
    /// </summary>
    public DateTime? ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the last used timestamp.
    /// </summary>
    public DateTime? LastUsedAt { get; private set; }

    /// <summary>
    /// Gets the revocation timestamp.
    /// </summary>
    public DateTime? RevokedAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who revoked this key.
    /// </summary>
    public Guid? RevokedBy { get; private set; }

    /// <summary>
    /// Gets the reason for revocation.
    /// </summary>
    public string? RevokeReason { get; private set; }

    /// <summary>
    /// Gets whether the API key is revoked.
    /// </summary>
    public bool IsRevoked => RevokedAt.HasValue;

    /// <summary>
    /// Gets whether the API key is expired.
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;

    /// <summary>
    /// Gets whether the API key is valid (not revoked and not expired).
    /// </summary>
    public bool IsValid => !IsRevoked && !IsExpired;

    private ApiKey() : base()
    {
    }

    public ApiKey(
        Guid id,
        Guid applicationId,
        string name,
        string? description,
        string keyPrefix,
        string keyHash,
        string environment,
        int rateLimitPerMinute,
        int rateLimitPerDay,
        string? allowedIps,
        string? allowedOrigins,
        DateTime createdAt,
        Guid createdBy,
        DateTime? expiresAt,
        DateTime? lastUsedAt,
        DateTime? revokedAt,
        Guid? revokedBy,
        string? revokeReason) : base(id)
    {
        ApplicationId = applicationId;
        Name = name;
        Description = description;
        KeyPrefix = keyPrefix;
        KeyHash = keyHash;
        Environment = environment;
        RateLimitPerMinute = rateLimitPerMinute;
        RateLimitPerDay = rateLimitPerDay;
        AllowedIps = allowedIps;
        AllowedOrigins = allowedOrigins;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ExpiresAt = expiresAt;
        LastUsedAt = lastUsedAt;
        RevokedAt = revokedAt;
        RevokedBy = revokedBy;
        RevokeReason = revokeReason;
    }

    public static ApiKey Create(
        Guid applicationId,
        string name,
        string keyPrefix,
        string keyHash,
        Guid createdBy,
        string? description = null,
        string environment = "production",
        int rateLimitPerMinute = 60,
        int rateLimitPerDay = 10000,
        string? allowedIps = null,
        string? allowedOrigins = null,
        DateTime? expiresAt = null)
    {
        return new ApiKey
        {
            ApplicationId = applicationId,
            Name = name,
            Description = description,
            KeyPrefix = keyPrefix,
            KeyHash = keyHash,
            Environment = environment,
            RateLimitPerMinute = rateLimitPerMinute,
            RateLimitPerDay = rateLimitPerDay,
            AllowedIps = allowedIps,
            AllowedOrigins = allowedOrigins,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
            ExpiresAt = expiresAt
        };
    }

    public void Revoke(Guid revokedBy, string? reason = null)
    {
        RevokedAt = DateTime.UtcNow;
        RevokedBy = revokedBy;
        RevokeReason = reason;
    }

    public void RecordUsage()
    {
        LastUsedAt = DateTime.UtcNow;
    }
}
