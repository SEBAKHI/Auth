using Auth.Application.Configuration;
using Auth.Infrastructure.AccountDeletion;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Auth_API.Common.HealthChecks;

/// <summary>
/// Readiness check on the retention sweep of this process: it reports the
/// registration's failure status when no sweep has succeeded for longer than a
/// working sweep ever leaves (<see cref="RetentionSweepStatus.MaxHealthyGap"/>).
/// </summary>
/// <remarks>
/// <para>
/// Registered as Degraded, which still answers 200, so the gateway's probe and
/// the service stay up. The description is one of three constants: never a
/// time, a count or an exception message, since <c>/ready</c> is anonymous.
/// </para>
/// <para>
/// What it cannot see: the status lives in memory, so a recycle starts it over,
/// and a process younger than the window is Healthy whatever its sweep did
/// (the pending description says so until the first success). The gateway's
/// <c>/ready</c> shows Healthy while the API answers 200, Degraded or not. The
/// dependable signals of a failing sweep are its failure row in the audit log,
/// written at most once a day while it fails, and the Error lines in the log.
/// </para>
/// </remarks>
public sealed class RetentionSweepHealthCheck : IHealthCheck
{
    internal const string CurrentDescription = "Retention sweep ran within its window.";
    internal const string PendingDescription = "Retention sweep has not completed since this process started.";
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
        try
        {
            if (_status.IsOverdue(_timeProvider.GetUtcNow().UtcDateTime, _settings.CurrentValue.WorkerPollInterval))
            {
                return Task.FromResult(new HealthCheckResult(context.Registration.FailureStatus, OverdueDescription));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                _status.LastSuccessUtc is null ? PendingDescription : CurrentDescription));
        }
        catch (Exception ex)
        {
            // Caught here, not by the health-check service: that one makes the
            // exception's message the entry's description, and the description
            // reaches anonymous callers even with error details switched off.
            return Task.FromResult(new HealthCheckResult(context.Registration.FailureStatus, OverdueDescription, ex));
        }
    }
}
