using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents a webhook key for authenticating webhook endpoint callers.
/// Uses HMAC-SHA256 hashing for deterministic, fast key lookup.
/// </summary>
public class WebhookKey : EntityBase
{
    /// <summary>
    /// Gets the ID of the application this webhook key belongs to.
    /// </summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>
    /// Gets the name of this webhook key.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the description of this webhook key.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the prefix of the key for identification (e.g., "wk_prod_").
    /// </summary>
    public string KeyPrefix { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the HMAC-SHA256 hash of the full key.
    /// </summary>
    public string KeyHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the target URL this webhook key authenticates for.
    /// </summary>
    public string TargetUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the environment (production, staging, development).
    /// </summary>
    public string Environment { get; private set; } = "production";

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
    /// Gets whether the webhook key is revoked.
    /// </summary>
    public bool IsRevoked => RevokedAt.HasValue;

    /// <summary>
    /// Gets whether the webhook key is expired.
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;

    /// <summary>
    /// Gets whether the webhook key is valid (not revoked and not expired).
    /// </summary>
    public bool IsValid => !IsRevoked && !IsExpired;

    private WebhookKey() : base()
    {
    }

    public WebhookKey(
        Guid id,
        Guid applicationId,
        string name,
        string? description,
        string keyPrefix,
        string keyHash,
        string targetUrl,
        string environment,
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
        TargetUrl = targetUrl;
        Environment = environment;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ExpiresAt = expiresAt;
        LastUsedAt = lastUsedAt;
        RevokedAt = revokedAt;
        RevokedBy = revokedBy;
        RevokeReason = revokeReason;
    }

    public static WebhookKey Create(
        Guid applicationId,
        string name,
        string keyPrefix,
        string keyHash,
        string targetUrl,
        Guid createdBy,
        string? description = null,
        string environment = "production",
        DateTime? expiresAt = null)
    {
        return new WebhookKey
        {
            ApplicationId = applicationId,
            Name = name,
            Description = description,
            KeyPrefix = keyPrefix,
            KeyHash = keyHash,
            TargetUrl = targetUrl,
            Environment = environment,
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
