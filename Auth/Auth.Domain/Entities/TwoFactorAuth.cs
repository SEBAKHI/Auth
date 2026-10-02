using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents a user's two-factor authentication configuration.
/// </summary>
public class TwoFactorAuth : EntityBase
{
    /// <summary>
    /// Consecutive failed verifications after which the factor locks.
    /// </summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>
    /// How long the factor stays locked once <see cref="MaxFailedAttempts"/> is reached.
    /// </summary>
    public const int LockoutMinutes = 15;

    /// <summary>
    /// Gets the ID of the user.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the encrypted TOTP secret key.
    /// </summary>
    public string SecretKey { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the JSON array of hashed recovery codes.
    /// </summary>
    public string? RecoveryCodes { get; private set; }

    /// <summary>
    /// Gets whether 2FA is enabled for this user.
    /// </summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when 2FA was enabled.
    /// </summary>
    public DateTime? EnabledAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when 2FA was last used successfully.
    /// </summary>
    public DateTime? LastUsedAt { get; private set; }

    /// <summary>
    /// Gets the number of failed 2FA attempts.
    /// </summary>
    public int FailedAttempts { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp until which 2FA is locked.
    /// </summary>
    public DateTime? LockedUntil { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this record was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; private set; }

    /// <summary>
    /// Gets whether 2FA is locked due to too many failed attempts.
    /// </summary>
    public bool IsLocked => LockedUntil.HasValue && LockedUntil.Value > DateTime.UtcNow;

    private TwoFactorAuth() : base()
    {
    }

    public TwoFactorAuth(
        Guid id,
        Guid userId,
        string secretKey,
        string? recoveryCodes,
        bool isEnabled,
        DateTime? enabledAt,
        DateTime? lastUsedAt,
        int failedAttempts,
        DateTime? lockedUntil,
        DateTime createdAt,
        DateTime? modifiedAt) : base(id)
    {
        UserId = userId;
        SecretKey = secretKey;
        RecoveryCodes = recoveryCodes;
        IsEnabled = isEnabled;
        EnabledAt = enabledAt;
        LastUsedAt = lastUsedAt;
        FailedAttempts = failedAttempts;
        LockedUntil = lockedUntil;
        CreatedAt = createdAt;
        ModifiedAt = modifiedAt;
    }

    /// <summary>
    /// Creates a new 2FA setup (not yet enabled).
    /// </summary>
    public static TwoFactorAuth Create(Guid userId, string secretKey)
    {
        return new TwoFactorAuth
        {
            UserId = userId,
            SecretKey = secretKey,
            RecoveryCodes = null,
            IsEnabled = false,
            EnabledAt = null,
            LastUsedAt = null,
            FailedAttempts = 0,
            LockedUntil = null,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = null
        };
    }

    // No Enable, Disable or failure-count method: every change of state — switching
    // the factor on or off, counting a failed code, settling one — is a
    // conditional statement of ITwoFactorStateStore whose affected-row count is the
    // decision. A method here would compute the change from a read, and two
    // requests that read the same row both pass any check made on what they read.
}
