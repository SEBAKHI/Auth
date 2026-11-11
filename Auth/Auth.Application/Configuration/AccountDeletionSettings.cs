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

    /// <summary>Shortest worker poll interval applied, in minutes.</summary>
    public const int MinWorkerPollMinutes = 1;

    /// <summary>Longest worker poll interval applied, in minutes (one day).</summary>
    public const int MaxWorkerPollMinutes = 1440;

    /// <summary>
    /// Fallback poll interval of the deletion worker. The worker reads
    /// <see cref="WorkerPollInterval"/>, held between
    /// <see cref="MinWorkerPollMinutes"/> and <see cref="MaxWorkerPollMinutes"/>:
    /// zero would spin the loop without a pause, and a negative value would
    /// throw out of the delay and stop the worker.
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

    /// <summary>Shortest login-attempt retention the sweep applies, in days.</summary>
    public const int MinLoginAttemptRetentionDays = 30;

    /// <summary>Shortest delivered-mail retention the sweep applies, in days.</summary>
    public const int MinOutboxRetentionDays = 30;

    /// <summary>
    /// Shortest audit-log retention the sweep applies, in days: the three years
    /// the published privacy policy commits to.
    /// </summary>
    public const int MinAuditLogRetentionDays = 1095;

    /// <summary>
    /// Shortest identifier reservation the settings accept, in days. Equal to the
    /// audit floor, since the effective window is never shorter than the audit
    /// retention anyway.
    /// </summary>
    public const int MinIdentifierReservationDays = MinAuditLogRetentionDays;

    /// <summary>Longest retention of any kind the sweep applies, in days (ten years).</summary>
    public const int MaxRetentionDays = 3650;

    /// <summary>
    /// Retention of (anonymized) LoginAttempts rows before the sweep purges them.
    /// The sweep reads <see cref="EffectiveLoginAttemptRetentionDays"/>.
    /// </summary>
    public int LoginAttemptRetentionDays { get; set; } = 365;

    /// <summary>
    /// Retention of Sent notification-outbox rows before the sweep purges them.
    /// The sweep reads <see cref="EffectiveOutboxRetentionDays"/>.
    /// </summary>
    public int OutboxRetentionDays { get; set; } = 180;

    /// <summary>
    /// Retention of AuditLogs rows before the sweep purges them. Default 1095
    /// days = the three years the published privacy policy commits to; the
    /// settings registry and <see cref="EffectiveAuditLogRetentionDays"/> both
    /// floor it there so an operator cannot shorten the period below what users
    /// were told.
    /// <para>
    /// A ceiling is mandatory, not optional: an unbounded audit log is an
    /// undisclosed indefinite retention period, and it also makes any finite
    /// identifier-reservation window unsound — an address can only be safely
    /// released once every record still keyed to it has expired.
    /// </para>
    /// </summary>
    public int AuditLogRetentionDays { get; set; } = 1095;

    /// <summary>
    /// How long a destroyed identifier stays reserved before the sweep deletes
    /// its tombstone and the address becomes registrable again.
    /// <para>
    /// This is a quarantine period, not a punishment, and its length is derived
    /// rather than chosen: an address may only be released once every record
    /// still keyed to it has expired. The effective window is therefore never
    /// shorter than <see cref="AuditLogRetentionDays"/> — see
    /// <c>EffectiveIdentifierReservationDays</c>, which enforces that at the
    /// sweep so no console value can break the invariant.
    /// </para>
    /// <para>
    /// The previous behaviour was "permanent", which could not be reconciled
    /// with the published claim that the record is anonymous: a keyed digest is
    /// reversible by anyone holding the key, and the key must be kept for the
    /// reservation to work at all. A bounded window is what makes the promise
    /// and the implementation the same statement.
    /// </para>
    /// </summary>
    public int IdentifierReservationDays { get; set; } = 1095;

    /// <summary>
    /// Version stamped on tombstones written by this process, recording which
    /// identifier HMAC key produced the digest. Deliberately NOT exposed in the
    /// settings console: it is meaningless on its own and only ever changes
    /// together with the key material itself.
    /// </summary>
    public byte IdentifierKeyVersion { get; set; } = 1;

    /// <summary>
    /// The login-attempt retention actually applied by the sweep, held between
    /// <see cref="MinLoginAttemptRetentionDays"/> and <see cref="MaxRetentionDays"/>.
    /// </summary>
    /// <remarks>
    /// The bounds live here, not only in the console: a value from a settings
    /// file or an environment variable never passes through the console. Zero
    /// would move the cutoff to today and delete every attempt the lockout
    /// counts; a value too large for <see cref="DateTime.AddDays"/> would throw
    /// on every run and stop the whole sweep.
    /// </remarks>
    public int EffectiveLoginAttemptRetentionDays =>
        Math.Clamp(LoginAttemptRetentionDays, MinLoginAttemptRetentionDays, MaxRetentionDays);

    /// <summary>
    /// The delivered-mail retention actually applied by the sweep, held between
    /// <see cref="MinOutboxRetentionDays"/> and <see cref="MaxRetentionDays"/>.
    /// </summary>
    public int EffectiveOutboxRetentionDays =>
        Math.Clamp(OutboxRetentionDays, MinOutboxRetentionDays, MaxRetentionDays);

    /// <summary>
    /// The audit-log retention actually applied by the sweep, held between
    /// <see cref="MinAuditLogRetentionDays"/> and <see cref="MaxRetentionDays"/>.
    /// </summary>
    /// <remarks>
    /// Before this bound existed the floor was enforced by the console alone, so
    /// a typo in a settings file (109 for 1095, or 0) reached the sweep as it
    /// was, and 0 deleted the whole audit history on the next start.
    /// </remarks>
    public int EffectiveAuditLogRetentionDays =>
        Math.Clamp(AuditLogRetentionDays, MinAuditLogRetentionDays, MaxRetentionDays);

    /// <summary>
    /// The reservation window actually applied by the sweep: never shorter than
    /// the audit-log retention period, because releasing an address while
    /// records still keyed to it survive is what lets a new holder inherit the
    /// previous one's history; never longer than <see cref="MaxRetentionDays"/>.
    /// </summary>
    public int EffectiveIdentifierReservationDays =>
        Math.Min(Math.Max(IdentifierReservationDays, EffectiveAuditLogRetentionDays), MaxRetentionDays);

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
    public TimeSpan WorkerPollInterval =>
        TimeSpan.FromMinutes(Math.Clamp(WorkerPollMinutes, MinWorkerPollMinutes, MaxWorkerPollMinutes));
}
