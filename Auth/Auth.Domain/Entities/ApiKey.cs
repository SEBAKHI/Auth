using Auth.Domain.Errors;
using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Domain.Entities;

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
    /// Gets the random selector the key carries in clear (OI-128 PR B), by which validation finds
    /// this row before its one hash verify; null for a key minted before that format, which no
    /// longer validates.
    /// </summary>
    public string? KeySelector { get; private set; }

    /// <summary>
    /// Gets the environment (production, staging, development).
    /// </summary>
    public string Environment { get; private set; } = "production";

    /// <summary>
    /// Gets the addresses the key may be presented from; empty means any. Reported by validation,
    /// and applied by the application's server, which sees the caller's address (OI-128 PR B).
    /// </summary>
    /// <remarks>
    /// The table's per-key rate limits and CORS origins are not modelled: nothing enforced them,
    /// and their columns keep their defaults (owner decision D-128-3).
    /// </remarks>
    public IpAllowList AllowedIps { get; private set; } = IpAllowList.Any;

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
        string? keySelector,
        string environment,
        IpAllowList allowedIps,
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
        KeySelector = keySelector;
        Environment = environment;
        AllowedIps = allowedIps;
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
        string keySelector,
        string keyHash,
        Guid createdBy,
        string? description = null,
        string environment = "production",
        IpAllowList? allowedIps = null,
        DateTime? expiresAt = null)
    {
        return new ApiKey
        {
            ApplicationId = applicationId,
            Name = name,
            Description = description,
            KeyPrefix = keyPrefix,
            KeySelector = keySelector,
            KeyHash = keyHash,
            Environment = environment,
            AllowedIps = allowedIps ?? IpAllowList.Any,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>
    /// The key that replaces this one at rotation: the same application, environment, addresses and
    /// expiry, under new material. The caller copies the scopes, and schedules this key's end with
    /// <see cref="ScheduleExpiration"/>.
    /// </summary>
    public ApiKey CreateReplacement(string keyPrefix, string keySelector, string keyHash, Guid rotatedBy, DateTime rotatedAt)
    {
        if (IsRevoked)
            throw new InvalidOperationException("Cannot replace a revoked API key.");

        return new ApiKey
        {
            ApplicationId = ApplicationId,
            Name = $"{Name} (rotated)",
            Description = $"Rotated from key {KeyPrefix}... on {rotatedAt:yyyy-MM-dd HH:mm:ss} UTC",
            KeyPrefix = keyPrefix,
            KeySelector = keySelector,
            KeyHash = keyHash,
            Environment = Environment,
            AllowedIps = AllowedIps,
            CreatedAt = rotatedAt,
            CreatedBy = rotatedBy,
            ExpiresAt = ExpiresAt
        };
    }

    /// <summary>
    /// Confirms that a key of <paramref name="applicationId"/> may carry <paramref name="scopes"/>
    /// (OI-128 PR B): at least one, and each an active permission of that application.
    /// </summary>
    /// <remarks>
    /// The key's application is told the scopes when it validates the key, and reads them as its
    /// own permissions. A platform code there, or another application's, would mean something to
    /// no one, or the wrong thing to the wrong application; a key with none authorizes nothing an
    /// application can check. Mirrors <see cref="Role.EnsureCanHold"/>.
    /// </remarks>
    public static ErrorOr<Success> EnsureCanCarry(Guid applicationId, IReadOnlyCollection<Permission> scopes)
    {
        if (scopes.Count == 0)
        {
            return ApiKeyErrors.ScopesRequired;
        }

        return scopes.All(permission => permission.IsActive && permission.ApplicationId == applicationId)
            ? Result.Success
            : ApiKeyErrors.ScopeNotForApplication;
    }

    public void Revoke(Guid revokedBy, string? reason = null)
    {
        RevokedAt = DateTime.UtcNow;
        RevokedBy = revokedBy;
        RevokeReason = reason;
    }

    /// <summary>
    /// Schedules the API key to expire at the specified time.
    /// Used during key rotation to keep the old key valid during the grace period.
    /// </summary>
    public void ScheduleExpiration(DateTime expiresAt)
    {
        if (IsRevoked)
            throw new InvalidOperationException("Cannot schedule expiration for a revoked API key.");

        ExpiresAt = expiresAt;
    }

    public void RecordUsage()
    {
        LastUsedAt = DateTime.UtcNow;
    }
}
