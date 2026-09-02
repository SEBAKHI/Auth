using System.Security.Cryptography;
using System.Text;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.SystemSettings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Internal.Controllers;

/// <summary>
/// Serves the handful of settings the API Gateway process consumes, so a value
/// saved in the system-settings console reaches BOTH processes.
/// <para>
/// The gateway is a separate process with its own configuration tree. Three
/// mechanisms can carry a value across that boundary: shared storage, push, or
/// pull. Shared storage means handing database credentials to the most exposed
/// component in the system. Push means the API must know where every gateway
/// instance lives, which breaks the moment there is more than one. Pull adds no
/// new dependency direction — the gateway already proxies to this API and
/// already probes its /ready — and no new credential, because the shared gateway
/// token both processes hold is enough to authenticate the call.
/// </para>
/// <para>
/// Deliberately NOT routed through the gateway's own allowlist: it is a
/// server-to-server endpoint the gateway calls directly, and forwarding it would
/// publish it to the internet for no reason.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/internal/gateway-settings")]
[AllowAnonymous]
public sealed class GatewayRuntimeSettingsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IOptionsSnapshot<GatewaySettings> _gatewaySettings;
    private readonly ISystemSettingsReloader _reloader;

    public GatewayRuntimeSettingsController(
        IConfiguration configuration,
        IOptionsSnapshot<GatewaySettings> gatewaySettings,
        ISystemSettingsReloader reloader)
    {
        _configuration = configuration;
        _gatewaySettings = gatewaySettings;
        _reloader = reloader;
    }

    /// <summary>
    /// Returns the gateway-consumed settings as they are RIGHT NOW, plus the
    /// settings version so the caller can skip work when nothing changed.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(GatewayRuntimeSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Get()
    {
        // Authenticated by the shared gateway token, and checked here rather
        // than relying on GatewayTokenValidationMiddleware: that middleware is
        // a no-op whenever Gateway:ValidationEnabled is false (as it is in
        // development), which would leave this endpoint open.
        if (!HasValidGatewayToken())
        {
            return Unauthorized();
        }

        // Read live from configuration so the response reflects the database
        // layer, exactly like the API's own DynamicCorsPolicyProvider. Empty
        // entries are array-shrink tombstones and are dropped here so the
        // gateway never has to know that detail.
        var origins = _configuration.GetSection("Cors:AllowedOrigins").GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        return Ok(new GatewayRuntimeSettingsResponse(
            Version: _reloader.Version,
            CorsAllowedOrigins: origins,
            CorsAllowCredentials: _configuration.GetValue("Cors:AllowCredentials", false),
            HealthChecksExposeErrorDetails: _configuration.GetValue("HealthChecks:ExposeErrorDetails", false),
            RateLimits: ReadRateLimits()));
    }

    /// <summary>
    /// The gateway's own limiter values, read live from the same configuration
    /// stack as everything else here. Defaults mirror the registry and the
    /// gateway's file layer; GatewayRateLimitingParityTests keeps the three in
    /// step.
    /// </summary>
    private GatewayRateLimitsResponse ReadRateLimits() => new(
        GlobalPermitLimit: _configuration.GetValue("GatewayRateLimiting:GlobalPermitLimit", 1000),
        GlobalWindowSeconds: _configuration.GetValue("GatewayRateLimiting:GlobalWindowSeconds", 60),
        GlobalQueueLimit: _configuration.GetValue("GatewayRateLimiting:GlobalQueueLimit", 100),
        AuthPermitLimit: _configuration.GetValue("GatewayRateLimiting:AuthPermitLimit", 20),
        AuthWindowSeconds: _configuration.GetValue("GatewayRateLimiting:AuthWindowSeconds", 60),
        RegisterPermitLimit: _configuration.GetValue("GatewayRateLimiting:RegisterPermitLimit", 200),
        RegisterWindowSeconds: _configuration.GetValue("GatewayRateLimiting:RegisterWindowSeconds", 60),
        ApiPermitLimit: _configuration.GetValue("GatewayRateLimiting:ApiPermitLimit", 100),
        ApiWindowSeconds: _configuration.GetValue("GatewayRateLimiting:ApiWindowSeconds", 60),
        AdminPermitLimit: _configuration.GetValue("GatewayRateLimiting:AdminPermitLimit", 120),
        AdminWindowSeconds: _configuration.GetValue("GatewayRateLimiting:AdminWindowSeconds", 60));

    private bool HasValidGatewayToken()
    {
        var settings = _gatewaySettings.Value;
        var expected = settings.ExpectedToken;

        // No token provisioned means no caller can be authenticated. Refuse
        // rather than serve, so a misconfigured deployment fails closed.
        if (string.IsNullOrEmpty(expected))
        {
            return false;
        }

        if (!Request.Headers.TryGetValue(settings.TokenHeaderName, out var presented))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var presentedBytes = Encoding.UTF8.GetBytes(presented.ToString());

        return expectedBytes.Length == presentedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, presentedBytes);
    }
}

/// <summary>
/// The gateway's view of the settings it consumes. Intentionally a closed list:
/// the gateway gets exactly the keys it applies, never the whole settings tree.
/// </summary>
/// <param name="Version">
/// The database settings-layer version. Unchanged version means unchanged
/// values, so the caller can skip rebuilding anything.
/// </param>
public sealed record GatewayRuntimeSettingsResponse(
    int Version,
    IReadOnlyList<string> CorsAllowedOrigins,
    bool CorsAllowCredentials,
    bool HealthChecksExposeErrorDetails,
    GatewayRateLimitsResponse RateLimits);

/// <summary>
/// The gateway's rate-limiter numbers. They live in the console because the
/// gateway process cannot reach the database; this pull is the only way a
/// saved value gets there.
/// </summary>
public sealed record GatewayRateLimitsResponse(
    int GlobalPermitLimit,
    int GlobalWindowSeconds,
    int GlobalQueueLimit,
    int AuthPermitLimit,
    int AuthWindowSeconds,
    int RegisterPermitLimit,
    int RegisterWindowSeconds,
    int ApiPermitLimit,
    int ApiWindowSeconds,
    int AdminPermitLimit,
    int AdminWindowSeconds);
