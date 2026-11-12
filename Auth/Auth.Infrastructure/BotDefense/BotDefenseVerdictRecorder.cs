using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.BotDefense;

/// <summary>
/// Writes one line per bot-check verdict (S13, BD12) and counts it for the posture.
/// </summary>
/// <remarks>
/// <para>
/// The line's words are fixed, so <c>Tools/probes/verdict-window.mjs</c> can count the verdicts
/// from the text log: <c>Bot defense verdict: action {action} verdict {verdict} mode {mode}
/// origin {origin} hostname {hostname} codes {codes} attempts {n} elapsedMs {ms}</c>. It names
/// the surface, never the token, the secret or the visitor's address.
/// </para>
/// <para>
/// The origin is the request's <c>Origin</c> when it is one of the listed apps
/// (<see cref="BotDefenseOrigins"/>), <c>other</c> otherwise. Written at Information, a check
/// that could not run at Warning: the owner's gate counts these lines with
/// <c>Serilog:MinimumLevel:Default</c> at Information.
/// </para>
/// </remarks>
public sealed class BotDefenseVerdictRecorder : IBotDefenseVerdictRecorder
{
    /// <summary>The message template. Its words are read by the counting script: change both together.</summary>
    public const string Template =
        "Bot defense verdict: action {BotAction} verdict {BotVerdict} mode {BotMode} origin {BotOrigin} hostname {BotHostname} codes {BotCodes} attempts {BotAttempts} elapsedMs {BotElapsedMs}";

    private readonly IRequestOriginContext _origin;
    private readonly IOptionsMonitor<IdentityProviderSettings> _identityProvider;
    private readonly IConfiguration _configuration;
    private readonly BotDefenseVerdictCounts _counts;
    private readonly ILogger<BotDefenseVerdictRecorder> _logger;

    public BotDefenseVerdictRecorder(
        IRequestOriginContext origin,
        IOptionsMonitor<IdentityProviderSettings> identityProvider,
        IConfiguration configuration,
        BotDefenseVerdictCounts counts,
        ILogger<BotDefenseVerdictRecorder> logger)
    {
        _origin = origin;
        _identityProvider = identityProvider;
        _configuration = configuration;
        _counts = counts;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Record(string action, BotDefenseMode mode, BotDefenseVerdict verdict)
    {
        var origin = BotDefenseOrigins.From(_identityProvider.CurrentValue, _configuration).Label(_origin.Origin);
        _counts.Add(origin, mode, verdict.Kind);

        var level = verdict.Kind is BotDefenseVerdictKind.Unavailable or BotDefenseVerdictKind.Misconfigured
            ? LogLevel.Warning
            : LogLevel.Information;

        _logger.Log(
            level,
            Template,
            action,
            verdict.Kind.ToString(),
            mode.ToString(),
            origin,
            verdict.Hostname ?? "-",
            verdict.Codes.Count > 0 ? string.Join(",", verdict.Codes) : "-",
            verdict.Attempts,
            verdict.ElapsedMs);
    }
}
