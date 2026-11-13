using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Auth.Infrastructure.BotDefense;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security;

/// <summary>
/// The bot check's posture (S13, BD12), each line named by its identifier, evaluated after every
/// reload and every minute (REVIEW 167 F1): the spikes follow the traffic, not the settings.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>bot.mode</c>: in Production, Off is Critical (nothing is checked), Monitor a Warning
/// (it measures and protects nobody), Enforce Information.</item>
/// <item><c>bot.fail-mode</c>: Information.</item>
/// <item><c>bot.keys-missing</c>: a sitekey or a secret missing while the mode is not Off:
/// Critical in Production, a Warning elsewhere; Information while Off.</item>
/// <item><c>bot.testing-key-in-production</c>: Critical; the check then refuses every
/// submission it cannot judge by the fail mode.</item>
/// <item><c>bot.siteverify-reachability</c>: with a secret set, the public dummy token is sent
/// with it. An answer without a secret error means Cloudflare answers and knows the secret; a
/// secret error is Critical in Production; no answer is a Warning in Production. Probed at most
/// once in five minutes, and its time is written, which the owner reads before Monitor.</item>
/// <item><c>bot.monitor-pass-rate</c>: per app, since this process started (Information); a
/// token no real widget makes is left out and its number said.</item>
/// <item><c>bot.token-missing-spike</c>: under Enforce, more than half of at least 20
/// submissions from the listed apps in ten minutes without a token: Critical (a page that no
/// longer sends one).</item>
/// <item><c>bot.local-rejection-spike</c>: under Enforce, more than half of at least 20 such
/// submissions refused for the deployment's own setup (a host, an action, a clock): Critical.</item>
/// <item><c>bot.siteverify-throttled</c>: Cloudflare answered 429 in the last ten minutes: a
/// Warning (volume can open the breaker, and the fail mode then applies).</item>
/// </list>
/// The reachability probe's outcome also feeds <see cref="BotDefenseOutageSignal"/> (OD2).
/// The counts are this process's (D5); the owner's gate reads every process's log lines.
/// </remarks>
public sealed class BotDefensePostureContributor : ISecurityPostureContributor
{
    /// <summary>How long one reachability probe answers for.</summary>
    public static readonly TimeSpan ProbeCacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>How often the posture check evaluates this contributor with no reload (REVIEW 167 F1).</summary>
    public static readonly TimeSpan Cadence = TimeSpan.FromMinutes(1);

    private const string ModeTemplate = "Bot defense posture bot.mode: {Mode}";
    private const string FailModeTemplate = "Bot defense posture bot.fail-mode: {FailMode}";
    private const string KeysMissingTemplate = "Bot defense posture bot.keys-missing: {Missing} (mode {Mode})";
    private const string TestingKeyTemplate =
        "Bot defense posture bot.testing-key-in-production: {Which} is one of Cloudflare's testing keys, which pass every check; the check refuses to judge until the deployment's own keys are set";
    private const string ReachableTemplate =
        "Bot defense posture bot.siteverify-reachability: Cloudflare answered in {ElapsedMs} ms";
    private const string SecretRejectedTemplate =
        "Bot defense posture bot.siteverify-reachability: Cloudflare refused the secret ({Codes}) after {ElapsedMs} ms; set the widget's secret again";
    private const string UnreachableTemplate =
        "Bot defense posture bot.siteverify-reachability: no answer from Cloudflare ({Failure}) after {ElapsedMs} ms";
    private const string PassRateTemplate = "Bot defense posture bot.monitor-pass-rate: {PassRates}";
    private const string SpikeTemplate =
        "Bot defense posture bot.token-missing-spike: {Missing} of {Submissions} submissions from the listed apps in the last 10 minutes carried no token";
    private const string ThrottledTemplate =
        "Bot defense posture bot.siteverify-throttled: Cloudflare answered 429 (too many requests) {Count} time(s) in the last 10 minutes; while it does, the circuit breaker can open and the fail mode applies";
    private const string LocalRejectionTemplate =
        "Bot defense posture bot.local-rejection-spike: {Local} of {Submissions} submissions from the listed apps in the last 10 minutes were refused for this deployment's own setup (hostname, action or challenge age); check the widget's hosts against IdentityProvider:FirstPartySpaOrigins, else Cors:AllowedOrigins, and the server's clock";

    private readonly IOptionsMonitor<BotDefenseSettings> _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEnvironmentInfo _environment;
    private readonly BotDefenseVerdictCounts _counts;
    private readonly BotDefenseCircuitBreaker _breaker;
    private readonly BotDefenseOutageSignal _outage;
    private readonly TimeProvider _timeProvider;
    private (DateTimeOffset At, string Secret, SecurityPostureProbe Probe, string? Codes)? _lastProbe;

    public BotDefensePostureContributor(
        IOptionsMonitor<BotDefenseSettings> settings,
        IServiceScopeFactory scopeFactory,
        IEnvironmentInfo environment,
        BotDefenseVerdictCounts counts,
        BotDefenseCircuitBreaker breaker,
        BotDefenseOutageSignal outage,
        TimeProvider timeProvider)
    {
        _settings = settings;
        _scopeFactory = scopeFactory;
        _environment = environment;
        _counts = counts;
        _breaker = breaker;
        _outage = outage;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string Name => "bot-defense";

    /// <summary>Every minute: the spikes and the throttling follow the traffic (REVIEW 167 F1).</summary>
    public TimeSpan? ReevaluateEvery => Cadence;

    /// <inheritdoc />
    public async Task<SecurityPosture> EvaluateAsync(CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        var production = _environment.IsProduction;
        var siteKey = settings.SiteKey?.Trim() ?? string.Empty;
        var secret = settings.TurnstileSecret?.Trim() ?? string.Empty;
        var lines = new List<SecurityPostureLine>();
        var reasons = new List<string>();
        var state = new List<string> { settings.Mode.ToString(), settings.FailMode.ToString(), production.ToString() };

        // bot.mode
        var modeLevel = !production
            ? LogLevel.Information
            : settings.Mode switch
            {
                BotDefenseMode.Off => LogLevel.Critical,
                BotDefenseMode.Monitor => LogLevel.Warning,
                _ => LogLevel.Information,
            };
        lines.Add(new SecurityPostureLine(modeLevel, ModeTemplate, [settings.Mode.ToString()]));
        if (modeLevel > LogLevel.Information)
        {
            reasons.Add("bot.mode");
        }

        // bot.fail-mode
        lines.Add(new SecurityPostureLine(LogLevel.Information, FailModeTemplate, [settings.FailMode.ToString()]));

        // bot.keys-missing
        var missing = new List<string>();
        if (siteKey.Length == 0)
        {
            missing.Add("BotDefense:SiteKey");
        }

        if (secret.Length == 0)
        {
            missing.Add("BotDefense:TurnstileSecret (the console's Secrets page, then one API restart)");
        }

        if (missing.Count > 0)
        {
            var level = settings.Mode == BotDefenseMode.Off
                ? LogLevel.Information
                : production ? LogLevel.Critical : LogLevel.Warning;
            lines.Add(new SecurityPostureLine(level, KeysMissingTemplate, [string.Join("; ", missing), settings.Mode.ToString()]));
            if (level > LogLevel.Information)
            {
                reasons.Add("bot.keys-missing");
            }

            state.Add("missing:" + string.Join("|", missing));
        }

        // bot.testing-key-in-production
        if (production)
        {
            var testing = new List<string>();
            if (TurnstileTestingKeys.IsTestingKey(siteKey))
            {
                testing.Add("BotDefense:SiteKey");
            }

            if (TurnstileTestingKeys.IsTestingKey(secret))
            {
                testing.Add("BotDefense:TurnstileSecret");
            }

            if (testing.Count > 0)
            {
                lines.Add(new SecurityPostureLine(LogLevel.Critical, TestingKeyTemplate, [string.Join(" and ", testing)]));
                reasons.Add("bot.testing-key-in-production");
                state.Add("testing:" + string.Join("|", testing));
            }
        }

        // bot.siteverify-reachability
        SecurityPostureProbe? probe = null;
        if (secret.Length > 0)
        {
            var (result, codes) = await ProbeAsync(secret, cancellationToken);
            probe = result;
            state.Add("probe:" + result.Outcome);
            if (result.Succeeded)
            {
                lines.Add(new SecurityPostureLine(LogLevel.Information, ReachableTemplate, [result.ElapsedMs]));
            }
            else if (result.Outcome == "SecretRejected")
            {
                lines.Add(new SecurityPostureLine(
                    production ? LogLevel.Critical : LogLevel.Warning, SecretRejectedTemplate, [codes, result.ElapsedMs]));
                reasons.Add("bot.siteverify-reachability");
            }
            else
            {
                lines.Add(new SecurityPostureLine(
                    production ? LogLevel.Warning : LogLevel.Information, UnreachableTemplate, [result.Outcome, result.ElapsedMs]));
                if (production)
                {
                    reasons.Add("bot.siteverify-reachability");
                }
            }
        }

        // bot.monitor-pass-rate
        var rates = _counts.PassRates();
        var rateText = rates.Count == 0
            ? "no submission yet"
            : string.Join("; ", rates.OrderBy(rate => rate.Key, StringComparer.Ordinal).Select(rate => PassRateText(rate.Key, rate.Value)));
        lines.Add(new SecurityPostureLine(LogLevel.Information, PassRateTemplate, [rateText]));

        // bot.token-missing-spike and bot.local-rejection-spike
        if (settings.Mode == BotDefenseMode.Enforce)
        {
            var (submissions, missingTokens, localRejected) = _counts.EnforcedWindow();
            var enough = submissions >= BotDefenseVerdictCounts.SpikeMinimumSubmissions;
            var spike = enough && missingTokens * 2 > submissions;
            lines.Add(new SecurityPostureLine(
                spike ? LogLevel.Critical : LogLevel.Information, SpikeTemplate, [missingTokens, submissions]));
            if (spike)
            {
                reasons.Add("bot.token-missing-spike");
                state.Add("spike");
            }

            // Refused people, not robots: a host missing from the accepted origins, for instance,
            // turns every sign-in from one app into TokenRejected, which no fail mode softens.
            var localSpike = enough && localRejected * 2 > submissions;
            lines.Add(new SecurityPostureLine(
                localSpike ? LogLevel.Critical : LogLevel.Information, LocalRejectionTemplate, [localRejected, submissions]));
            if (localSpike)
            {
                reasons.Add("bot.local-rejection-spike");
                state.Add("local-rejections");
            }
        }

        // bot.siteverify-throttled
        var throttled = _breaker.ThrottledInWindow();
        if (throttled > 0)
        {
            lines.Add(new SecurityPostureLine(LogLevel.Warning, ThrottledTemplate, [throttled]));
            reasons.Add("bot.siteverify-throttled");
            state.Add("throttled");
        }

        return new SecurityPosture(
            reasons.Count > 0 ? SecurityPostureLevel.Weak : SecurityPostureLevel.Ok,
            reasons,
            lines,
            string.Join('|', state),
            probe);
    }

    private static string PassRateText(string origin, (long Submissions, long Passed, long Junk) rate)
    {
        var share = rate.Submissions == 0 ? "-" : $"{100.0 * rate.Passed / rate.Submissions:0.0} %";
        var junk = rate.Junk == 0 ? "" : $", {rate.Junk} junk token(s) not counted";
        return $"{origin} {rate.Passed}/{rate.Submissions} ({share}){junk}";
    }

    /// <summary>
    /// Sends the public dummy token with the secret, at most once in five minutes for one secret.
    /// Outcomes: <c>Reachable</c>, <c>SecretRejected</c>, or the kind of failure.
    /// </summary>
    private async Task<(SecurityPostureProbe Probe, string? Codes)> ProbeAsync(string secret, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_lastProbe is { } last && last.Secret == secret && now - last.At < ProbeCacheDuration)
        {
            return (last.Probe, last.Codes);
        }

        using var scope = _scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ITurnstileSiteverifyClient>();
        var result = await client.VerifyAsync(secret, TurnstileTestingKeys.DummyToken, cancellationToken);

        (SecurityPostureProbe, string?) outcome = result.Answer switch
        {
            null => (new SecurityPostureProbe(result.Failure ?? "Unreachable", result.ElapsedMs), null),
            // The probe's token is well formed, so bad-request here is the server's own request.
            { ErrorCodes: var codes } when codes.Any(code => code is "missing-input-secret" or "invalid-input-secret" or "bad-request") =>
                (new SecurityPostureProbe("SecretRejected", result.ElapsedMs), string.Join(",", codes)),
            _ => (new SecurityPostureProbe(SecurityPostureProbe.Reachable, result.ElapsedMs), null),
        };

        _lastProbe = (now, secret, outcome.Item1, outcome.Item2);
        // A refused secret is an answer: only no answer at all reads as an outage (OD2).
        _outage.RecordProbe(answered: result.Answer is not null);
        return outcome;
    }
}
