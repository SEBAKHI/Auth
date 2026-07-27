namespace Auth.Application.Configuration;

/// <summary>
/// Configuration for the account deletion lifecycle (grace window, worker
/// cadence, retention constants and the identifier-hash key).
/// </summary>
public class AccountDeletionSettings
{
    public const string SectionName = "AccountDeletion";

    /// <summary>
    /// Length of the recovery window between the deletion request and
    /// irreversible execution.
    /// </summary>
    public int GraceDays { get; set; } = 30;

    /// <summary>
    /// Fallback poll interval of the deletion worker.
    /// </summary>
    public int WorkerPollMinutes { get; set; } = 15;

    /// <summary>
    /// Maximum due requests executed per worker cycle.
    /// </summary>
    public int WorkerBatchSize { get; set; } = 25;

    /// <summary>
    /// Execution attempts before a deletion request dead-letters as Failed.
    /// </summary>
    public int MaxExecutionAttempts { get; set; } = 5;

    /// <summary>
    /// Lifetime of a deletion re-authentication OTP.
    /// </summary>
    public int OtpExpirationMinutes { get; set; } = 15;

    /// <summary>
    /// Retention of (anonymized) LoginAttempts rows before the sweep purges them.
    /// </summary>
    public int LoginAttemptRetentionDays { get; set; } = 365;

    /// <summary>
    /// Retention of Sent notification-outbox rows before the sweep purges them.
    /// </summary>
    public int OutboxRetentionDays { get; set; } = 180;

    /// <summary>
    /// Retention-policy version stamped on tombstones and deletion requests
    /// (format "YYYY.MM"). Bump when the documented retention policy changes.
    /// </summary>
    public string PolicyVersion { get; set; } = "2026.07";

    /// <summary>
    /// When true, the one-time encryption migration runs at startup: TOTP
    /// secrets and phone numbers are re-encrypted under per-user DEKs.
    /// Idempotent (only rows without the v2: prefix are touched). Enable for
    /// one deployment, verify the logged report, then disable.
    /// </summary>
    public bool RunEncryptionMigration { get; set; } = false;

    /// <summary>
    /// Base64 HMAC-SHA256 key (>= 32 bytes) for identifier hashing in the
    /// tombstone registry. Provisioned via SecretManagement (auto-generated in
    /// PlainText mode; encrypted secrets file otherwise) — never committed.
    /// PERMANENT: reservations and restore re-application depend on stable
    /// hashes, so this key is never rotated.
    /// </summary>
    public string? IdentifierHmacKeyPlain { get; set; }

    public TimeSpan GracePeriod => TimeSpan.FromDays(GraceDays);
    public TimeSpan WorkerPollInterval => TimeSpan.FromMinutes(WorkerPollMinutes);
}
