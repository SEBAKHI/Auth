namespace Auth.Domain.ValueObjects;

/// <summary>
/// A user's two-factor row as one read saw it, with the TOTP secret still
/// encrypted. It answers only what a read may answer — whether the factor is on,
/// whether it looked locked — and never stands in for the conditional writes that
/// decide when requests race.
/// </summary>
public sealed record class TwoFactorSnapshot
{
    public TwoFactorSnapshot(
        Guid userId,
        string protectedSecretKey,
        string? recoveryCodes,
        bool isEnabled,
        int failedAttempts,
        DateTime? lockedUntil)
    {
        UserId = userId;
        ProtectedSecretKey = protectedSecretKey;
        RecoveryCodes = recoveryCodes;
        IsEnabled = isEnabled;
        FailedAttempts = failedAttempts;
        LockedUntil = lockedUntil;
    }

    /// <summary>
    /// Gets the ID of the user the row belongs to.
    /// </summary>
    public Guid UserId { get; }

    /// <summary>
    /// Gets the TOTP secret exactly as stored: encrypted. The read never decrypts
    /// it, so a later write can compare against the stored text itself.
    /// </summary>
    public string ProtectedSecretKey { get; }

    /// <summary>
    /// Gets the JSON array of recovery-code hashes, byte for byte as stored.
    /// </summary>
    public string? RecoveryCodes { get; }

    /// <summary>
    /// Gets whether two-factor authentication was enabled when read.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// Gets the failed-verification count when read.
    /// </summary>
    public int FailedAttempts { get; }

    /// <summary>
    /// Gets the UTC time until which the factor was locked when read.
    /// </summary>
    public DateTime? LockedUntil { get; }

    /// <summary>
    /// Gets whether the factor was locked when read. A fast path only: the attempt
    /// reservation checks the lock again in the same statement that counts.
    /// </summary>
    public bool IsLocked => LockedUntil.HasValue && LockedUntil.Value > DateTime.UtcNow;

    // The secret's ciphertext and the recovery-code hashes stay out of any log
    // line or assertion message the snapshot reaches.
    public override string ToString() => $"TwoFactorSnapshot {{ UserId = {UserId}, IsEnabled = {IsEnabled} }}";
}
