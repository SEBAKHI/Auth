using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security;

/// <summary>
/// The account-deletion and retention settings whose configured value lies outside the bounds
/// the code applies: one Warning line per value, naming what was configured and what is applied.
/// </summary>
/// <remarks>
/// <para>
/// The bounds are enforced where the values are read (<c>AccountDeletionSettings.Effective*</c>,
/// <c>DataRetentionSettings.WorkerPollInterval</c>), because a settings file, an environment
/// variable or a stored override reaches the binder without the console's bounds. Enforcing them
/// silently would leave the console showing the configured value as the one in force; this line
/// is how an operator learns the value was not taken as written.
/// </para>
/// <para>
/// The state is the list of configured values outside their bounds, so a reload that changes
/// nothing here writes nothing, and correcting a value writes the Ok state once.
/// </para>
/// </remarks>
public sealed class RetentionBoundsPostureContributor : ISecurityPostureContributor
{
    /// <summary>At least one value is applied other than as configured.</summary>
    public const string OutsideBoundsReason = "retention.value-outside-bounds";

    private const string OutsideTemplate =
        "{Setting} = {Configured} is outside {Min}–{Max}: the value applied is {Applied}. Set a value within the bounds";

    private readonly IOptionsMonitor<AccountDeletionSettings> _accountDeletion;
    private readonly IOptionsMonitor<DataRetentionSettings> _dataRetention;

    public RetentionBoundsPostureContributor(
        IOptionsMonitor<AccountDeletionSettings> accountDeletion,
        IOptionsMonitor<DataRetentionSettings> dataRetention)
    {
        _accountDeletion = accountDeletion;
        _dataRetention = dataRetention;
    }

    /// <inheritdoc />
    public string Name => "retention.bounds";

    /// <inheritdoc />
    public Task<SecurityPosture> EvaluateAsync(CancellationToken cancellationToken)
    {
        var a = _accountDeletion.CurrentValue;
        var d = _dataRetention.CurrentValue;
        const string ad = AccountDeletionSettings.SectionName;

        Bound[] bounds =
        [
            new($"{ad}:{nameof(a.GraceDays)}", a.GraceDays,
                AccountDeletionSettings.MinGraceDays, AccountDeletionSettings.MaxGraceDays, a.EffectiveGraceDays),
            new($"{ad}:{nameof(a.OtpExpirationMinutes)}", a.OtpExpirationMinutes,
                AccountDeletionSettings.MinOtpExpirationMinutes, AccountDeletionSettings.MaxOtpExpirationMinutes,
                a.EffectiveOtpExpirationMinutes),
            new($"{ad}:{nameof(a.WorkerPollMinutes)}", a.WorkerPollMinutes,
                AccountDeletionSettings.MinWorkerPollMinutes, AccountDeletionSettings.MaxWorkerPollMinutes,
                a.EffectiveWorkerPollMinutes),
            new($"{ad}:{nameof(a.WorkerBatchSize)}", a.WorkerBatchSize,
                AccountDeletionSettings.MinWorkerBatchSize, AccountDeletionSettings.MaxWorkerBatchSize,
                a.EffectiveWorkerBatchSize),
            new($"{ad}:{nameof(a.MaxExecutionAttempts)}", a.MaxExecutionAttempts,
                AccountDeletionSettings.MinMaxExecutionAttempts, AccountDeletionSettings.MaxMaxExecutionAttempts,
                a.EffectiveMaxExecutionAttempts),
            new($"{ad}:{nameof(a.LoginAttemptRetentionDays)}", a.LoginAttemptRetentionDays,
                AccountDeletionSettings.MinLoginAttemptRetentionDays, AccountDeletionSettings.MaxRetentionDays,
                a.EffectiveLoginAttemptRetentionDays),
            new($"{ad}:{nameof(a.OutboxRetentionDays)}", a.OutboxRetentionDays,
                AccountDeletionSettings.MinOutboxRetentionDays, AccountDeletionSettings.MaxRetentionDays,
                a.EffectiveOutboxRetentionDays),
            new($"{ad}:{nameof(a.AuditLogRetentionDays)}", a.AuditLogRetentionDays,
                AccountDeletionSettings.MinAuditLogRetentionDays, AccountDeletionSettings.MaxRetentionDays,
                a.EffectiveAuditLogRetentionDays),
            // Raised to the audit retention by design when shorter; only a value
            // outside its own bounds is reported.
            new($"{ad}:{nameof(a.IdentifierReservationDays)}", a.IdentifierReservationDays,
                AccountDeletionSettings.MinIdentifierReservationDays, AccountDeletionSettings.MaxRetentionDays,
                a.EffectiveIdentifierReservationDays),
            new($"{DataRetentionSettings.SectionName}:{nameof(d.WorkerPollMinutes)}", d.WorkerPollMinutes,
                DataRetentionSettings.MinWorkerPollMinutes, DataRetentionSettings.MaxWorkerPollMinutes,
                d.EffectiveWorkerPollMinutes),
        ];

        var outside = bounds.Where(b => b.Configured < b.Min || b.Configured > b.Max).ToList();
        if (outside.Count == 0)
        {
            return Task.FromResult(new SecurityPosture(SecurityPostureLevel.Ok, [], [], "ok"));
        }

        var lines = outside
            .Select(b => new SecurityPostureLine(
                LogLevel.Warning, OutsideTemplate, [b.Setting, b.Configured, b.Min, b.Max, b.Applied]))
            .ToList();

        return Task.FromResult(new SecurityPosture(
            SecurityPostureLevel.Weak,
            [OutsideBoundsReason],
            lines,
            string.Join(",", outside.Select(b => $"{b.Setting}={b.Configured}"))));
    }

    private sealed record Bound(string Setting, int Configured, int Min, int Max, int Applied);
}
