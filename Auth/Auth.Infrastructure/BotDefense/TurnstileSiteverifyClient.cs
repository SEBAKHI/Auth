using System.Text.Json;
using Auth.Application.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.BotDefense;

/// <summary>Cloudflare's answer to one verification, as siteverify returned it.</summary>
/// <param name="Success">Whether Cloudflare accepted the token.</param>
/// <param name="ErrorCodes">Cloudflare's error codes; empty on success.</param>
/// <param name="Hostname">The host the token was made on.</param>
/// <param name="Action">The action the widget was rendered with.</param>
/// <param name="ChallengeTs">When the challenge was solved.</param>
public sealed record SiteverifyAnswer(
    bool Success,
    IReadOnlyList<string> ErrorCodes,
    string? Hostname,
    string? Action,
    DateTimeOffset? ChallengeTs);

/// <summary>One verification: Cloudflare's answer, or why there was none.</summary>
/// <param name="Answer">The answer, or null when none was reached.</param>
/// <param name="Failure">Why no answer was reached (<c>timeout</c>, <c>transport</c>, <c>http-503</c>, <c>breaker-open</c>…).</param>
/// <param name="Attempts">Calls made: 0 (breaker open), 1 or 2.</param>
/// <param name="ElapsedMs">Time spent, both attempts included.</param>
public sealed record SiteverifyResult(SiteverifyAnswer? Answer, string? Failure, int Attempts, long ElapsedMs);

/// <summary>Cloudflare Turnstile's siteverify endpoint (S13, BD4).</summary>
public interface ITurnstileSiteverifyClient
{
    /// <summary>Asks Cloudflare whether <paramref name="token"/> is good, with <paramref name="secret"/>.</summary>
    Task<SiteverifyResult> VerifyAsync(string secret, string token, CancellationToken cancellationToken);
}

/// <summary>
/// Calls <c>https://challenges.cloudflare.com/turnstile/v0/siteverify</c> under one deadline.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Sends the secret, the token and an idempotency key, and nothing else: never the
/// visitor's address (IP10).</item>
/// <item>One deadline, <c>BotDefense:SiteverifyTimeoutMs</c> read per call, covers both attempts;
/// it is applied here rather than as <see cref="HttpClient.Timeout"/> so the setting stays hot.
/// Running out of it is final.</item>
/// <item>One second attempt, with the same idempotency key, after a transport failure or a 5xx,
/// and only while half the deadline remains: Cloudflare answers a repeated key as it answered
/// the first, so a token is never spent twice.</item>
/// <item>A 429, and a 4xx without error codes, end the call: no second attempt.</item>
/// <item>The per-process breaker (<see cref="BotDefenseCircuitBreaker"/>) counts calls without an
/// answer; while it is open nothing is called.</item>
/// </list>
/// A failure is returned, never thrown; only the caller's own cancellation propagates.
/// </remarks>
public sealed class TurnstileSiteverifyClient : ITurnstileSiteverifyClient
{
    /// <summary>Cloudflare's siteverify address.</summary>
    public static readonly Uri Endpoint = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    /// <summary>The largest answer read. Cloudflare's is a few hundred bytes.</summary>
    public const int MaxResponseBytes = 64 * 1024;

    /// <summary>
    /// The client's own timeout: above the largest deadline, so the per-call deadline, not the
    /// client, decides.
    /// </summary>
    public static readonly TimeSpan ClientTimeout =
        TimeSpan.FromMilliseconds(BotDefenseSettings.MaxSiteverifyTimeoutMs + 1000);

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<BotDefenseSettings> _settings;
    private readonly BotDefenseCircuitBreaker _breaker;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TurnstileSiteverifyClient> _logger;

    public TurnstileSiteverifyClient(
        HttpClient httpClient,
        IOptionsMonitor<BotDefenseSettings> settings,
        BotDefenseCircuitBreaker breaker,
        TimeProvider timeProvider,
        ILogger<TurnstileSiteverifyClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _breaker = breaker;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SiteverifyResult> VerifyAsync(string secret, string token, CancellationToken cancellationToken)
    {
        if (!_breaker.TryEnter())
        {
            return new SiteverifyResult(null, "breaker-open", 0, 0);
        }

        var started = _timeProvider.GetTimestamp();
        var timeout = _settings.CurrentValue.SiteverifyTimeout;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        // One key per token: the second attempt repeats it, so Cloudflare answers it as the first.
        var idempotencyKey = Guid.NewGuid().ToString();
        var attempts = 0;
        string failure;

        while (true)
        {
            attempts++;
            var retryable = false;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                {
                    Content = new FormUrlEncodedContent(
                    [
                        new KeyValuePair<string, string>("secret", secret),
                        new KeyValuePair<string, string>("response", token),
                        new KeyValuePair<string, string>("idempotency_key", idempotencyKey),
                    ]),
                };

                using var response = await _httpClient.SendAsync(request, deadline.Token);
                var status = (int)response.StatusCode;

                if (status >= 500)
                {
                    failure = $"http-{status}";
                    retryable = true;
                }
                else
                {
                    var answer = await ReadAnswerAsync(response, deadline.Token);
                    // A 2xx with an answer, and a 4xx (not 429) that names its error codes, are
                    // answers; anything else carries nothing to judge the token by.
                    if (answer is not null && (response.IsSuccessStatusCode || (status != 429 && answer.ErrorCodes.Count > 0)))
                    {
                        _breaker.RecordAnswer();
                        return new SiteverifyResult(answer, null, attempts, Elapsed(started));
                    }

                    failure = answer is null && response.IsSuccessStatusCode ? "unreadable-answer" : $"http-{status}";
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The deadline ran out: final, whatever attempt this was.
                failure = "timeout";
                break;
            }
            catch (OperationCanceledException)
            {
                // The caller went away: not an outage, and not counted as one.
                _breaker.Abandon();
                throw;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogDebug(ex, "Bot defense: siteverify transport failure on attempt {Attempt}", attempts);
                failure = "transport";
                retryable = true;
            }

            if (!retryable || attempts >= 2 || _timeProvider.GetElapsedTime(started) > timeout / 2)
            {
                break;
            }
        }

        _breaker.RecordFailure(failure);
        return new SiteverifyResult(null, failure, attempts, Elapsed(started));
    }

    private long Elapsed(long started) => (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds;

    /// <summary>The answer in the body, or null when the body is not one.</summary>
    private static async Task<SiteverifyAnswer?> ReadAnswerAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var success)
                || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return null;
            }

            var codes = root.TryGetProperty("error-codes", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Where(code => code.ValueKind == JsonValueKind.String).Select(code => code.GetString()!).ToList()
                : [];

            DateTimeOffset? challengeTs = root.TryGetProperty("challenge_ts", out var ts)
                && ts.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(ts.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null;

            return new SiteverifyAnswer(
                success.GetBoolean(),
                codes,
                root.TryGetProperty("hostname", out var host) && host.ValueKind == JsonValueKind.String ? host.GetString() : null,
                root.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String ? action.GetString() : null,
                challengeTs);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
