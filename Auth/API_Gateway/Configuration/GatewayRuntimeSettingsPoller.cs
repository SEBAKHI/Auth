using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace API_Gateway.Configuration;

/// <summary>
/// Keeps <see cref="GatewayRuntimeSettingsProvider"/> in step with the Auth API,
/// so a value saved in the system-settings console reaches this process too.
/// <para>
/// Polls rather than receives a push: the API would otherwise have to know the
/// address of every gateway instance, which stops working the moment there is
/// more than one or an instance restarts. A poll costs one small request per
/// interval against a dependency this process already has.
/// </para>
/// <para>
/// Every failure keeps the last known good values. An unreachable API must
/// degrade to stale settings, never to no settings — the gateway is the edge,
/// and an edge that loses its CORS policy takes the whole product down.
/// </para>
/// </summary>
public sealed class GatewayRuntimeSettingsPoller : BackgroundService
{
    private readonly GatewayRuntimeSettingsProvider _provider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GatewayRuntimeSettingsPoller> _logger;
    private readonly Uri _endpoint;
    private readonly string? _gatewayToken;
    private readonly string _tokenHeaderName;
    private readonly TimeSpan _interval;

    private bool _loggedFailure;

    public GatewayRuntimeSettingsPoller(
        GatewayRuntimeSettingsProvider provider,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GatewayRuntimeSettingsPoller> logger)
    {
        _provider = provider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var baseUrl = configuration["Services:AuthApi:BaseUrl"]?.TrimEnd('/')
            ?? "http://localhost:5100";
        _endpoint = new Uri($"{baseUrl}/api/v1/internal/gateway-settings");

        _gatewayToken = configuration["Gateway:Token"];
        _tokenHeaderName = configuration["Gateway:TokenHeaderName"] ?? "X-Gateway-Token";
        _interval = TimeSpan.FromSeconds(
            Math.Clamp(configuration.GetValue("Services:AuthApi:SettingsPollSeconds", 30), 5, 3600));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_gatewayToken))
        {
            // Without the shared token the endpoint cannot authenticate us, so
            // polling would only produce 401s. Stay on the file layer and say so
            // once, rather than logging a failure every interval forever.
            _logger.LogInformation(
                "Gateway token is not configured; system-settings values stay on this process's configuration file.");
            return;
        }

        // A tick before the first delay: the process should not serve a stale
        // CORS policy for a whole interval after every restart.
        while (!stoppingToken.IsCancellationRequested)
        {
            await PollOnceAsync(stoppingToken);

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            request.Headers.TryAddWithoutValidation(_tokenHeaderName, _gatewayToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content
                .ReadFromJsonAsync<GatewayRuntimeSettingsPayload>(cancellationToken);

            if (payload is null)
            {
                return;
            }

            var current = _provider.Current;

            // Every field falls back to what this process already holds. Two
            // things arrive here as an absent field: an API deployed before
            // this gateway (rolling upgrade, so it does not know RateLimits
            // yet) and a truncated response. Neither is a reason to run the
            // edge on zeros — a PermitLimit of 0 rejects every request, which
            // is a worse outage than the stale value it replaced.
            var settings = new GatewayRuntimeSettings(
                Version: payload.Version,
                CorsAllowedOrigins: payload.CorsAllowedOrigins ?? current.CorsAllowedOrigins,
                CorsAllowCredentials: payload.CorsAllowCredentials,
                HealthChecksExposeErrorDetails: payload.HealthChecksExposeErrorDetails,
                RateLimits: IsUsable(payload.RateLimits) ? payload.RateLimits! : current.RateLimits);

            if (settings.Version != current.Version)
            {
                _provider.Update(settings);
                _logger.LogInformation(
                    "Applied system settings version {Version} from the Auth API ({OriginCount} CORS origins, admin limit {AdminPermitLimit}/{AdminWindowSeconds}s).",
                    settings.Version,
                    settings.CorsAllowedOrigins.Count,
                    settings.RateLimits.AdminPermitLimit,
                    settings.RateLimits.AdminWindowSeconds);
            }
            else
            {
                _provider.Update(settings);
            }

            _loggedFailure = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // Once per outage, not once per interval: a long API outage would
            // otherwise bury every other line in the gateway's log.
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                _logger.LogWarning(
                    ex,
                    "Could not read system settings from the Auth API; continuing on the last known values.");
            }
        }
    }

    /// <summary>
    /// Whether a fetched limit set can be run on. A window or permit count of
    /// zero would silence the whole edge, so a partially-populated payload is
    /// treated as no payload. A queue length of zero is legitimate — it means
    /// reject on arrival rather than hold.
    /// </summary>
    private static bool IsUsable(GatewayRateLimits? limits) =>
        limits is not null
        && limits.GlobalPermitLimit > 0 && limits.GlobalWindowSeconds > 0 && limits.GlobalQueueLimit >= 0
        && limits.AuthPermitLimit > 0 && limits.AuthWindowSeconds > 0
        && limits.ApiPermitLimit > 0 && limits.ApiWindowSeconds > 0
        && limits.AdminPermitLimit > 0 && limits.AdminWindowSeconds > 0;

    /// <summary>
    /// The wire shape, deliberately separate from <see cref="GatewayRuntimeSettings"/>:
    /// every field is optional here because the sender may be an older build,
    /// and the mapping above decides what an absent field falls back to. Reading
    /// straight into the runtime record would hand it nulls and zeros its type
    /// says are impossible.
    /// </summary>
    private sealed record GatewayRuntimeSettingsPayload(
        int Version,
        IReadOnlyList<string>? CorsAllowedOrigins,
        bool CorsAllowCredentials,
        bool HealthChecksExposeErrorDetails,
        GatewayRateLimits? RateLimits);
}
