using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security;

/// <summary>
/// The breached-password check's posture: the settings in force, whether they screen less than
/// the shipped defaults, and whether HIBP answers from this server.
/// </summary>
/// <remarks>
/// The settings can be weakened from the console without a restart, and HIBP can be unreachable
/// from a host with no outbound HTTPS, in which case every check waits for its deadline and then
/// lets the password through (FailOpen). Neither shows to a user, so both are stated in the log:
/// at Warning in Production, at Information elsewhere.
/// </remarks>
public sealed class BreachedPasswordPostureContributor : ISecurityPostureContributor
{
    /// <summary>
    /// A password HIBP lists (thousands of times), so a lookup that finds nothing means the answer
    /// did not come from HIBP. The probe calls the checker directly and never goes through the
    /// password validator.
    /// </summary>
    internal const string ProbePassword = "Summer2024!";

    /// <summary>The probe reached a service that answered, but not with HIBP's listing.</summary>
    internal const string NotListed = "NotListed";

    private const string PostureTemplate =
        "Breached-password posture: Enabled={Enabled}, Mode={Mode}, FailOpen={FailOpen}, RejectThreshold={RejectThreshold}, TimeoutMs={TimeoutMs}";
    private const string WeakenedTemplate = "Breached-password screening weakened in Production: {Reasons}";
    private const string ReachableTemplate = "HIBP probe call: reachable in {ElapsedMs} ms";
    private const string ProbeFailedTemplate = "HIBP probe call failed ({FailureKind}) after {ElapsedMs} ms";

    private readonly IOptionsMonitor<PasswordSettings> _passwordSettings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;

    public BreachedPasswordPostureContributor(
        IOptionsMonitor<PasswordSettings> passwordSettings,
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        TimeProvider timeProvider)
    {
        _passwordSettings = passwordSettings;
        _scopeFactory = scopeFactory;
        _environment = environment;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string Name => "password.breach-check";

    /// <inheritdoc />
    public async Task<SecurityPosture> EvaluateAsync(CancellationToken cancellationToken)
    {
        var settings = _passwordSettings.CurrentValue.BreachedPasswordCheck;
        var production = _environment.IsProduction();

        var lines = new List<SecurityPostureLine>
        {
            new(LogLevel.Information, PostureTemplate,
                [settings.Enabled, settings.Mode, settings.FailOpen, settings.RejectThreshold, settings.TimeoutMs]),
        };

        var weakenedBy = WeakenedBy(settings);
        if (production && weakenedBy.Count > 0)
        {
            lines.Add(new SecurityPostureLine(LogLevel.Warning, WeakenedTemplate, [string.Join("; ", weakenedBy)]));
        }

        SecurityPostureProbe? probe = null;
        if (settings.Enabled)
        {
            var (result, failure) = await ProbeAsync(cancellationToken);
            probe = result;
            lines.Add(probe.Succeeded
                ? new SecurityPostureLine(LogLevel.Information, ReachableTemplate, [probe.ElapsedMs])
                : new SecurityPostureLine(
                    production ? LogLevel.Warning : LogLevel.Information,
                    ProbeFailedTemplate,
                    [probe.Outcome, probe.ElapsedMs],
                    failure));
        }

        // Weak is a Production verdict: elsewhere the same facts are written as information only.
        var reasons = new List<string>();
        if (production)
        {
            reasons.AddRange(weakenedBy);
            if (probe is { Succeeded: false })
            {
                reasons.Add($"HIBP probe call failed ({probe.Outcome})");
            }
        }

        var state = string.Join('|',
            settings.Enabled, settings.Mode, settings.FailOpen, settings.RejectThreshold, settings.TimeoutMs,
            production, probe?.Outcome ?? "NotProbed");

        return new SecurityPosture(
            reasons.Count > 0 ? SecurityPostureLevel.Weak : SecurityPostureLevel.Ok,
            reasons,
            lines,
            state,
            probe);
    }

    /// <summary>
    /// Each way the settings screen fewer new passwords than the shipped defaults do. FailOpen=true
    /// is the shipped default (an HIBP outage must not block password changes), not a weakening;
    /// its effect shows in the probe line and in each check's own Warning.
    /// </summary>
    private static List<string> WeakenedBy(BreachedPasswordCheckSettings settings)
    {
        var reasons = new List<string>();
        if (!settings.Enabled)
        {
            reasons.Add("Enabled=False (no new password is checked)");
        }

        if (settings.Mode == BreachAction.Warn)
        {
            reasons.Add("Mode=Warn (a breached password is accepted)");
        }

        if (settings.TimeoutMs < BreachedPasswordCheckSettings.WarnBelowTimeoutMs)
        {
            reasons.Add(
                $"TimeoutMs={settings.TimeoutMs} (below {BreachedPasswordCheckSettings.WarnBelowTimeoutMs}: most checks end before HIBP answers)");
        }

        if (settings.RejectThreshold > 1)
        {
            reasons.Add(
                $"RejectThreshold={settings.RejectThreshold} (a password seen fewer times in breaches is accepted)");
        }

        return reasons;
    }

    /// <summary>
    /// One lookup of <see cref="ProbePassword"/> through the checker the password flows use, in a
    /// scope of its own, under the same per-call deadline (TimeoutMs) a real check gets.
    /// </summary>
    private async Task<(SecurityPostureProbe Probe, Exception? Failure)> ProbeAsync(CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        string outcome;
        Exception? failure = null;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var checker = scope.ServiceProvider.GetRequiredService<IBreachedPasswordChecker>();
            var count = await checker.GetBreachCountAsync(ProbePassword, cancellationToken);
            outcome = count > 0 ? SecurityPostureProbe.Reachable : NotListed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A cancellation the host did not request is the checker's deadline.
            outcome = ex is OperationCanceledException ? "Timeout" : "Error";
            failure = ex;
        }

        var elapsedMs = (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds;
        return (new SecurityPostureProbe(outcome, elapsedMs), failure);
    }
}
