using Auth.Application.Configuration;
using Auth.Infrastructure.AccountDeletion;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Auth_API.Common.HealthChecks;

/// <summary>
/// Readiness check that the daily retention sweep is still running: it reports the
/// registration's failure status when no sweep has succeeded for longer than a
/// working sweep ever leaves (<see cref="RetentionSweepStatus.MaxHealthyGap"/>).
/// </summary>
/// <remarks>
/// The sweep no longer writes an audit row on runs that delete nothing, so silence
/// in the audit log is the normal state. This check is what tells "nothing to
/// delete" apart from "not running": a worker that stopped, or a sweep failing on
/// every retry. It is registered as Degraded, which still answers 200, so the
/// gateway's probe and the service stay up. The description is constant: never a
/// time or a count, since <c>/ready</c> is anonymous.
/// </remarks>
public sealed class RetentionSweepHealthCheck : IHealthCheck
{
    internal const string CurrentDescription = "Retention sweep ran within its window.";
    internal const string OverdueDescription = "Retention sweep overdue.";

    private readonly RetentionSweepStatus _status;
    private readonly IOptionsMonitor<AccountDeletionSettings> _settings;
    private readonly TimeProvider _timeProvider;

    public RetentionSweepHealthCheck(
        RetentionSweepStatus status,
        IOptionsMonitor<AccountDeletionSettings> settings,
        TimeProvider timeProvider)
    {
        _status = status;
        _settings = settings;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var overdue = _status.IsOverdue(
            _timeProvider.GetUtcNow().UtcDateTime,
            _settings.CurrentValue.WorkerPollInterval);

        return Task.FromResult(overdue
            ? new HealthCheckResult(context.Registration.FailureStatus, OverdueDescription)
            : HealthCheckResult.Healthy(CurrentDescription));
    }
}
