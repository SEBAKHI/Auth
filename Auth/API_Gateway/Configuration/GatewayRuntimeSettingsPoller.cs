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

            var settings = await response.Content
                .ReadFromJsonAsync<GatewayRuntimeSettings>(cancellationToken);

            if (settings is null)
            {
                return;
            }

            if (settings.Version != _provider.Current.Version)
            {
                _provider.Update(settings);
                _logger.LogInformation(
                    "Applied system settings version {Version} from the Auth API ({OriginCount} CORS origins).",
                    settings.Version, settings.CorsAllowedOrigins.Count);
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
}
