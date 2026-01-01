using Auth_Lib.Foundation.Base;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents a user's two-factor authentication configuration.
/// </summary>
public class TwoFactorAuth : EntityBase
{
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

    /// <summary>
    /// Enables 2FA and stores recovery codes.
    /// </summary>
    public void Enable(string hashedRecoveryCodes)
    {
        IsEnabled = true;
        EnabledAt = DateTime.UtcNow;
        RecoveryCodes = hashedRecoveryCodes;
        ModifiedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Disables 2FA.
    /// </summary>
    public void Disable()
    {
        IsEnabled = false;
        RecoveryCodes = null;
        ModifiedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a successful 2FA verification.
    /// </summary>
    public void RecordSuccess()
    {
        LastUsedAt = DateTime.UtcNow;
        FailedAttempts = 0;
        LockedUntil = null;
        ModifiedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a failed 2FA verification.
    /// </summary>
    public void RecordFailure(int maxAttempts = 5, int lockoutMinutes = 15)
    {
        FailedAttempts++;
        if (FailedAttempts >= maxAttempts)
        {
            LockedUntil = DateTime.UtcNow.AddMinutes(lockoutMinutes);
        }
        ModifiedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Consumes a recovery code.
    /// </summary>
    public void UpdateRecoveryCodes(string remainingCodes)
    {
        RecoveryCodes = remainingCodes;
        ModifiedAt = DateTime.UtcNow;
    }
}
