using System.Collections.Concurrent;
using System.Threading.Channels;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Auth.Infrastructure.Security;

/// <summary>
/// Writes the deployment's security posture to the log: every registered
/// <see cref="ISecurityPostureContributor"/>, once after startup and again after every
/// configuration reload (a save in the console, the periodic refresh of the database overrides,
/// an edited file).
/// </summary>
/// <remarks>
/// <para>
/// A reload is global (any section, often fired twice for one save), so a contributor's lines
/// are written only when its <see cref="SecurityPosture.State"/> differs from the one last
/// written: the log carries changes, not a line per reload.
/// </para>
/// <para>
/// It never runs on a request path and never delays startup: the evaluation happens in
/// <see cref="ExecuteAsync"/>, after the host has started. Each contributor runs in its own
/// isolation boundary, so one that throws costs one Error line, leaves the others unaffected, and
/// never stops the host.
/// </para>
/// </remarks>
public sealed class SecurityPostureCheck : BackgroundService
{
    /// <summary>
    /// How long to wait after a reload before evaluating. The options caches the contributors read
    /// rebind on the same change token as this service, in no guaranteed order, and one save can
    /// fire the token more than once; the wait lets both settle into one evaluation.
    /// </summary>
    internal static readonly TimeSpan DefaultReloadSettleDelay = TimeSpan.FromSeconds(1);

    private readonly IReadOnlyList<ISecurityPostureContributor> _contributors;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SecurityPostureCheck> _logger;
    private readonly TimeSpan _reloadSettleDelay;

    // One pending reload is enough: every evaluation reads the configuration in force when it runs.
    private readonly Channel<bool> _reloads = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private readonly ConcurrentDictionary<string, string> _lastWrittenState = new(StringComparer.Ordinal);

    public SecurityPostureCheck(
        IEnumerable<ISecurityPostureContributor> contributors,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<SecurityPostureCheck> logger)
        : this(contributors, configuration, timeProvider, logger, DefaultReloadSettleDelay)
    {
    }

    internal SecurityPostureCheck(
        IEnumerable<ISecurityPostureContributor> contributors,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<SecurityPostureCheck> logger,
        TimeSpan reloadSettleDelay)
    {
        _contributors = contributors.ToList();
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
        _reloadSettleDelay = reloadSettleDelay;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off the startup path before anything else: a contributor may wait on a probe.
        await Task.Yield();

        using var reloads = ChangeToken.OnChange(
            _configuration.GetReloadToken, () => _reloads.Writer.TryWrite(true));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateAsync(stoppingToken);
                await WaitForNextEvaluationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// The one place that decides when the posture is evaluated again: today, after a
    /// configuration reload. A periodic re-statement of a non-Ok posture (S16) is a timer raced
    /// against the reload here.
    /// </summary>
    private async Task WaitForNextEvaluationAsync(CancellationToken stoppingToken)
    {
        await _reloads.Reader.ReadAsync(stoppingToken);
        await Task.Delay(_reloadSettleDelay, _timeProvider, stoppingToken);

        // A reload that arrived during the wait is covered by the evaluation that follows it.
        _reloads.Reader.TryRead(out _);
    }

    private Task EvaluateAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(_contributors.Select(contributor => EvaluateAsync(contributor, stoppingToken)));

    private async Task EvaluateAsync(ISecurityPostureContributor contributor, CancellationToken stoppingToken)
    {
        try
        {
            var posture = await contributor.EvaluateAsync(stoppingToken);

            if (_lastWrittenState.TryGetValue(contributor.Name, out var last) &&
                string.Equals(last, posture.State, StringComparison.Ordinal))
            {
                return;
            }

            foreach (var line in posture.Lines)
            {
#pragma warning disable CA2254 // Each line's template is a constant of its contributor.
                _logger.Log(line.Level, line.Exception, line.MessageTemplate, line.Arguments.ToArray());
#pragma warning restore CA2254
            }

            _lastWrittenState[contributor.Name] = posture.State;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            // Forget the last state, so the evaluation after a recovery writes its lines again.
            _lastWrittenState.TryRemove(contributor.Name, out _);
            _logger.LogError(ex, "Security posture contributor {Contributor} failed", contributor.Name);
        }
    }
}
