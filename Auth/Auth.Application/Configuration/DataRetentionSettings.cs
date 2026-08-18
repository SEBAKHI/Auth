namespace Auth.Application.Configuration;

/// <summary>
/// How long rows that have fallen out of use are kept before the retention
/// sweep removes them, and how hard that sweep is allowed to push.
/// </summary>
/// <remarks>
/// Retention here is NOT "how long the row stays valid" — every row this governs
/// is already dead. It is "how long the row stays USEFUL after it died", and the
/// use is almost always detection: a revoked refresh token is the only evidence
/// that turns a stolen token into a caught theft, and a consumed authorization
/// code is the only evidence that a code was replayed. Delete either too early
/// and the attack still happens, silently, with nothing left to notice it by.
/// <para>
/// Every window therefore has a FLOOR enforced in code, not in the console. A
/// zero or negative value would move the cutoff to now or later and sweep live
/// rows on the next run; the floors make that unreachable however the value is
/// set.
/// </para>
/// </remarks>
public class DataRetentionSettings
{
    public const string SectionName = "DataRetention";

    /// <summary>Kill switch. Off leaves every table untouched.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the worker wakes to check whether today's sweep has run.</summary>
    public int WorkerPollMinutes { get; set; } = 15;

    /// <summary>
    /// Rows per statement. 4000 sits below the ~5000 row locks at which SQL
    /// Server escalates to a table lock, which would block every live query
    /// against the table for the duration of the batch.
    /// </summary>
    public int BatchSize { get; set; } = 4000;

    /// <summary>
    /// Ceiling on rows removed from any one table in a single run.
    /// </summary>
    /// <remarks>
    /// Bounds the FIRST run, which is the dangerous one: these tables have
    /// accumulated since the system was deployed, so the opening sweep faces a
    /// backlog no later sweep will ever see. Whatever is left over is taken by
    /// the next run, and the one after, until the backlog is gone.
    /// </remarks>
    public int MaxRowsPerTablePerRun { get; set; } = 200_000;

    /// <summary>Authorization codes. They live ~60 seconds; the window is for investigating a replay.</summary>
    public int AuthorizationCodeDays { get; set; } = 7;

    /// <summary>Two-factor challenges. Long enough to investigate a guessing run.</summary>
    public int TwoFactorChallengeDays { get; set; } = 7;

    /// <summary>Password reset tokens.</summary>
    public int PasswordResetTokenDays { get; set; } = 7;

    /// <summary>Email verification tokens.</summary>
    public int EmailVerificationTokenDays { get; set; } = 7;

    /// <summary>IdP SSO sessions, counted from expiry or revocation.</summary>
    public int IdpSessionDays { get; set; } = 30;

    /// <summary>
    /// Refresh tokens, counted from expiry or revocation. The longest window,
    /// and the one that matters most.
    /// </summary>
    /// <remarks>
    /// Two things read revoked rows long after they die. The reuse detector
    /// treats a revoked row as proof of theft and answers with a full credential
    /// revocation plus a warning to the owner; once the row is gone the same
    /// stolen token reads as merely unknown and nobody is told. The dashboard
    /// separately reports revocations over a trailing window the console allows
    /// up to 90 days, so a shorter retention makes those figures quietly wrong
    /// rather than visibly missing.
    /// </remarks>
    public int RefreshTokenDays { get; set; } = 90;

    /// <summary>Poll interval as a TimeSpan, floored at one minute.</summary>
    public TimeSpan WorkerPollInterval => TimeSpan.FromMinutes(Math.Max(1, WorkerPollMinutes));

    /// <summary>Batch size clamped to a sane band.</summary>
    public int EffectiveBatchSize => Math.Clamp(BatchSize, 100, 4000);

    /// <summary>Per-table ceiling, floored at one batch so a run always progresses.</summary>
    public int EffectiveMaxRowsPerTablePerRun => Math.Max(EffectiveBatchSize, MaxRowsPerTablePerRun);

    public int EffectiveAuthorizationCodeDays => Math.Max(1, AuthorizationCodeDays);

    public int EffectiveTwoFactorChallengeDays => Math.Max(1, TwoFactorChallengeDays);

    public int EffectivePasswordResetTokenDays => Math.Max(1, PasswordResetTokenDays);

    public int EffectiveEmailVerificationTokenDays => Math.Max(1, EmailVerificationTokenDays);

    public int EffectiveIdpSessionDays => Math.Max(1, IdpSessionDays);

    /// <summary>
    /// Refresh-token retention, floored at 90 days — the longest trailing window
    /// the dashboard will report on. Below it the revocation figures go wrong
    /// without going blank, which is worse than losing them.
    /// </summary>
    public int EffectiveRefreshTokenDays => Math.Max(90, RefreshTokenDays);
}
