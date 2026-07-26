using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Zero-PII destruction registry entry written when an account is permanently
/// destroyed: {hashed identifier, deleted-at, policy version} and nothing else.
/// Rows are permanent — identifiers are never recycled, and restores re-apply
/// deletion from this registry.
/// </summary>
public class AccountDeletionTombstone : EntityBase
{
    /// <summary>
    /// Gets the HMAC-SHA256 hash of the account's normalized email. Unique:
    /// one tombstone per identifier, and the permanent reservation key checked
    /// by every registration path.
    /// </summary>
    public string EmailHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the HMAC-SHA256 hash of the upper-cased username, reserved for the
    /// same never-recycle guarantee.
    /// </summary>
    public string UsernameHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC instant of destruction.
    /// </summary>
    public DateTime DeletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the retention-policy version applied (format "YYYY.MM").
    /// </summary>
    public string PolicyVersion { get; private set; } = string.Empty;

    private AccountDeletionTombstone() : base()
    {
    }

    public AccountDeletionTombstone(
        Guid id,
        string emailHash,
        string usernameHash,
        DateTime deletedAtUtc,
        string policyVersion) : base(id)
    {
        EmailHash = emailHash;
        UsernameHash = usernameHash;
        DeletedAtUtc = deletedAtUtc;
        PolicyVersion = policyVersion;
    }

    /// <summary>
    /// Creates a tombstone for an account being destroyed now.
    /// </summary>
    /// <param name="emailHash">HMAC-SHA256 of the normalized email.</param>
    /// <param name="usernameHash">HMAC-SHA256 of the upper-cased username.</param>
    /// <param name="policyVersion">Retention-policy version in force.</param>
    public static AccountDeletionTombstone Create(
        string emailHash,
        string usernameHash,
        string policyVersion)
    {
        return new AccountDeletionTombstone
        {
            EmailHash = emailHash,
            UsernameHash = usernameHash,
            DeletedAtUtc = DateTime.UtcNow,
            PolicyVersion = policyVersion
        };
    }
}
