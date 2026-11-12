using System.Collections.Frozen;
using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.BotDefense;

/// <summary>
/// The Turnstile verdict for one token (S13, BD5): the configuration first, then the token, then
/// Cloudflare's answer.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Neither key set: <c>Unconfigured</c>, whatever the request carried. One key without the
/// other, or one of Cloudflare's testing keys in Production: <c>Misconfigured</c>. Nothing is
/// called in either case.</item>
/// <item>No token: <c>Missing</c>. A token longer than Cloudflare issues (2048), a token with
/// any character outside printable ASCII (Cloudflare's tokens have none), or the public testing
/// token in Production (which only a testing secret accepts; checking it here saves a call and
/// is not the control, the refusal of testing keys above is): <c>Rejected</c>, before any
/// call.</item>
/// <item>Cloudflare unreachable: <c>Unavailable</c>. Its two secret codes:
/// <c>Misconfigured</c>. <c>internal-error</c>: <c>Unavailable</c>. Any other refusal,
/// <c>bad-request</c> included: <c>Rejected</c>. A per-request answer is judged as the token's,
/// never as the server's state: a token that made Cloudflare answer <c>bad-request</c> must not
/// reach the outage allowance. The posture's probe, which sends a well-formed token, reports a
/// <c>bad-request</c> as the server's fault.</item>
/// <item>An accepted token must name an accepted host (<see cref="BotDefenseOrigins"/>), this
/// surface's action, and a challenge solved within 300 seconds of Cloudflare's answer, both
/// times by Cloudflare's clock (the server's own clock only when the answer has no date);
/// otherwise <c>Rejected</c>. Outside Production a testing secret's acceptance is enough: its
/// answer names no real host or action.</item>
/// </list>
/// </remarks>
public sealed class TurnstileBotDefenseVerifier : IBotDefenseVerifier
{
    /// <summary>The longest token Cloudflare issues.</summary>
    public const int MaxTokenLength = 2048;

    /// <summary>How long after the challenge a token is accepted.</summary>
    public static readonly TimeSpan MaxChallengeAge = TimeSpan.FromSeconds(300);

    /// <summary>Cloudflare's codes that mean the server's secret, not the visitor's token, is wrong.</summary>
    private static readonly FrozenSet<string> ConfigurationCodes =
        new[] { "missing-input-secret", "invalid-input-secret" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The refusals the deployment's own setup causes, not the visitor: an app host missing from
    /// the accepted origins, a page asking for another action, a clock far off. Logged at Warning,
    /// and counted for the posture's <c>bot.local-rejection-spike</c>.
    /// </summary>
    public static readonly FrozenSet<string> LocalRejectionCodes =
        new[] { "hostname-mismatch", "action-mismatch", "challenge-too-old" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Tokens no real widget makes: Cloudflare's <c>invalid-input-response</c> (a fake token), and
    /// the local refusals of a token too long, not printable ASCII, or the public dummy one in
    /// Production. Counted apart, never as a submission, so junk sent with a listed app's
    /// <c>Origin</c> neither lowers that app's pass rate nor dilutes a token-missing spike.
    /// </summary>
    public static readonly FrozenSet<string> JunkTokenCodes =
        new[] { "invalid-input-response", "token-too-long", "token-malformed", "dummy-token" }.ToFrozenSet(StringComparer.Ordinal);

    private readonly IOptionsMonitor<BotDefenseSettings> _settings;
    private readonly IOptionsMonitor<IdentityProviderSettings> _identityProvider;
    private readonly IConfiguration _configuration;
    private readonly ITurnstileSiteverifyClient _client;
    private readonly IEnvironmentInfo _environment;
    private readonly TimeProvider _timeProvider;

    public TurnstileBotDefenseVerifier(
        IOptionsMonitor<BotDefenseSettings> settings,
        IOptionsMonitor<IdentityProviderSettings> identityProvider,
        IConfiguration configuration,
        ITurnstileSiteverifyClient client,
        IEnvironmentInfo environment,
        TimeProvider timeProvider)
    {
        _settings = settings;
        _identityProvider = identityProvider;
        _configuration = configuration;
        _client = client;
        _environment = environment;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<BotDefenseVerdict> VerifyAsync(string? token, string action, CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        var siteKey = settings.SiteKey?.Trim() ?? string.Empty;
        var secret = settings.TurnstileSecret?.Trim() ?? string.Empty;
        var production = _environment.IsProduction;

        // The configuration before the token: an empty sitekey with a bound secret is a setup
        // fault (Misconfigured), not an absent check, so clearing the sitekey from the console
        // cannot switch the check off (ARR-0406).
        if (siteKey.Length == 0 && secret.Length == 0)
        {
            return BotDefenseVerdict.Local(BotDefenseVerdictKind.Unconfigured);
        }

        if (secret.Length == 0)
        {
            return BotDefenseVerdict.Local(BotDefenseVerdictKind.Misconfigured, "secret-missing");
        }

        if (siteKey.Length == 0)
        {
            return BotDefenseVerdict.Local(BotDefenseVerdictKind.Misconfigured, "sitekey-missing");
        }

        if (production && (TurnstileTestingKeys.IsTestingKey(siteKey) || TurnstileTestingKeys.IsTestingKey(secret)))
        {
            return BotDefenseVerdict.Local(BotDefenseVerdictKind.Misconfigured, "testing-key-in-production");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return BotDefenseVerdict.Local(BotDefenseVerdictKind.Missing);
        }

        if (token.Length > MaxTokenLength)
        {
            return BotDefenseVerdict.Local(BotDefenseVerdictKind.Rejected, "token-too-long");
        }

        if (token.Any(character => character is < '!' or > '~'))
        {
            return BotDefenseVerdict.Local(BotDefenseVerdictKind.Rejected, "token-malformed");
        }

        if (production && token == TurnstileTestingKeys.DummyToken)
        {
            return BotDefenseVerdict.Local(BotDefenseVerdictKind.Rejected, "dummy-token");
        }

        var result = await _client.VerifyAsync(secret, token, cancellationToken);
        if (result.Answer is not { } answer)
        {
            return new BotDefenseVerdict(
                BotDefenseVerdictKind.Unavailable, [result.Failure ?? "unavailable"], null, result.Attempts, result.ElapsedMs);
        }

        BotDefenseVerdict Verdict(BotDefenseVerdictKind kind, IReadOnlyList<string> codes) =>
            new(kind, codes, answer.Hostname, result.Attempts, result.ElapsedMs);

        if (!answer.Success)
        {
            if (answer.ErrorCodes.Any(ConfigurationCodes.Contains))
            {
                return Verdict(BotDefenseVerdictKind.Misconfigured, answer.ErrorCodes);
            }

            return answer.ErrorCodes.Contains("internal-error")
                ? Verdict(BotDefenseVerdictKind.Unavailable, answer.ErrorCodes)
                : Verdict(BotDefenseVerdictKind.Rejected, answer.ErrorCodes);
        }

        if (!production && TurnstileTestingKeys.IsTestingKey(secret))
        {
            return Verdict(BotDefenseVerdictKind.Passed, ["testing-secret"]);
        }

        if (!BotDefenseOrigins.From(_identityProvider.CurrentValue, _configuration).AcceptsHostname(answer.Hostname))
        {
            return Verdict(BotDefenseVerdictKind.Rejected, ["hostname-mismatch"]);
        }

        if (!string.Equals(answer.Action, action, StringComparison.Ordinal))
        {
            return Verdict(BotDefenseVerdictKind.Rejected, ["action-mismatch"]);
        }

        var answeredAt = answer.RespondedAt ?? _timeProvider.GetUtcNow();
        if (answer.ChallengeTs is not { } solvedAt || answeredAt - solvedAt > MaxChallengeAge)
        {
            return Verdict(BotDefenseVerdictKind.Rejected, ["challenge-too-old"]);
        }

        return Verdict(BotDefenseVerdictKind.Passed, []);
    }
}
