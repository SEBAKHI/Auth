using System.Threading.RateLimiting;
using API_Gateway.Configuration;
using API_Gateway.Middleware;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Auth_Localization.Extensions;
using Auth.Shared.Configuration;
using Auth.Shared.Diagnostics;
using Auth.Shared.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Machine-local overrides (including the PlainText-mode gateway token) live in
// appsettings.{Environment}.local.json, which is git-ignored; the committed environment file
// beside it carries only non-secret defaults. Must match the Auth API's layering.
builder.Configuration.AddEnvironmentLocalJsonFile(builder.Environment.EnvironmentName);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "API_Gateway")
    .CreateLogger();

builder.Host.UseSerilog();

// Configure Data Protection (used to decrypt the shared secrets file in Certificate/Dpapi modes).
// The storage mode must match the Auth API so both apps read the same gateway token.
// Defaults to %ProgramData%\AuthSystem\Keys (machine-wide so it matches the Auth API's ring);
// see AuthDataProtectionExtensions.ResolveKeyRingPath for why %LOCALAPPDATA% is unsafe under a
// service / IIS app-pool identity.
var dataProtectionPath = AuthDataProtectionExtensions.ResolveKeyRingPath(
    builder.Configuration["DataProtection:KeyPath"]);

var dpCertificateSettings = builder.Configuration
    .GetSection(DataProtectionCertificateSettings.SectionName)
    .Get<DataProtectionCertificateSettings>() ?? new DataProtectionCertificateSettings();

// Certificate is the shipped default so a real deployment matches the Auth API's default. The
// Development fallback to PlainText is the SAME rule the Auth API applies, which is what keeps
// the two in agreement in both cases: with a certificate both run Certificate, without one both
// run PlainText. Outside Development a missing certificate still fails fast in both apps.
var configuredStorageMode = AuthDataProtectionExtensions.ParseStorageMode(
    builder.Configuration["SecretManagement:StorageMode"]);
var storageMode = AuthDataProtectionExtensions.ResolveStorageMode(
    builder.Configuration["SecretManagement:StorageMode"],
    builder.Environment.IsDevelopment(),
    dpCertificateSettings);

if (storageMode != configuredStorageMode)
{
    Log.Warning(
        "SecretManagement:StorageMode is '{Configured}' but no DataProtection:Certificate is configured. " +
        "Falling back to {Effective} because this is the Development environment: the gateway token is read " +
        "from this app's own Gateway:Token setting instead of the shared encrypted secrets file. The Auth API " +
        "applies the same fallback, so the two still match. Non-Development environments fail to start instead.",
        configuredStorageMode, storageMode);
}

builder.Services.AddDataProtection()
    .SetApplicationName("AuthSystem")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .ConfigureKeyProtection(storageMode, dpCertificateSettings);

// In PlainText mode the gateway token comes from this app's own appsettings (Gateway:Token).
// In Certificate/Dpapi mode it is read from the shared encrypted secrets file.
if (storageMode != SecretStorageMode.PlainText)
{
    var tempServices = new ServiceCollection();
    tempServices.AddDataProtection()
        .SetApplicationName("AuthSystem")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
        .ConfigureKeyProtection(storageMode, dpCertificateSettings);

    using var tempProvider = tempServices.BuildServiceProvider();
    var dataProtectionProvider = tempProvider.GetRequiredService<IDataProtectionProvider>();

    // Add encrypted secrets to configuration (overrides appsettings.json values).
    // Use the same secrets-file path the Auth API uses (empty = default location).
    builder.Configuration.AddDpapiSecrets(
        dataProtectionProvider,
        builder.Configuration["SecretManagement:SecretFilePath"]);
}

// Gateway Configuration
var gatewayToken = builder.Configuration["Gateway:Token"];

// In production, gateway token must be configured (via DPAPI secrets file).
// Driven by the shared RequiredSecretsRegistry so this process and the Auth API
// declare their secrets in one place: the token the gateway stamps and the one
// the API expects are the same secret, and drift between them rejects every
// proxied request while both processes look healthy.
var missingGatewaySecrets = RequiredSecretsRegistry.FindMissing(
    key => builder.Configuration[key], RequiredSecretsRegistry.Gateway);

if (missingGatewaySecrets.Count > 0 && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        $"Refusing to start: missing required secret(s) [{string.Join(", ", missingGatewaySecrets.Select(s => s.ConfigurationKey))}]. " +
        "Ensure the encrypted secrets file contains the GatewayToken. File location: " +
        SecretConfigurationExtensions.GetDefaultSecretFilePath());
}

// YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(builderContext =>
    {
        // Add gateway token to all proxied requests (only if configured).
        //
        // The Remove is not defensive tidying and it deliberately sits OUTSIDE
        // the configured-token check. YARP copies inbound request headers by
        // default and nothing in this project's ReverseProxy configuration turns
        // that off, so a client that sends its own X-Gateway-Token has it
        // forwarded — and a bare Add would append to it, producing the two-value
        // header "theirs, ours". The API compares the header as a single string
        // (StringValues.ToString()), so that never matches and every such
        // request is rejected with 403. Stripping it unconditionally also means
        // a gateway running without a token cannot be used to smuggle one
        // through to the API. Same shape as the correlation-id transform below.
        builderContext.AddRequestTransform(context =>
        {
            context.ProxyRequest.Headers.Remove("X-Gateway-Token");
            if (!string.IsNullOrEmpty(gatewayToken))
            {
                context.ProxyRequest.Headers.Add("X-Gateway-Token", gatewayToken);
            }
            return ValueTask.CompletedTask;
        });

        // Forward client IP
        builderContext.AddXForwardedFor();
        builderContext.AddXForwardedHost();
        builderContext.AddXForwardedProto();

        // Add correlation ID
        builderContext.AddRequestTransform(context =>
        {
            var correlationId = context.HttpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                ?? Guid.NewGuid().ToString();

            context.ProxyRequest.Headers.Remove("X-Correlation-ID");
            context.ProxyRequest.Headers.Add("X-Correlation-ID", correlationId);

            return ValueTask.CompletedTask;
        });
    });

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Limits are read per request from GatewayRuntimeSettingsProvider, not once
    // from this process's configuration file: they are console-owned settings
    // that arrive over the settings pull, and a value frozen here would ignore
    // every save until the next deployment.
    //
    // Partition keys carry the settings version for the same reason the Auth
    // API's do — a partition caches its limiter on first hit, so without the
    // stamp a saved change would sit unapplied until every open window idled
    // out, and an administrator would reasonably conclude the setting is fake.
    static GatewayRateLimits Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<GatewayRuntimeSettingsProvider>().Current.RateLimits;

    static int SettingsVersion(HttpContext context) =>
        context.RequestServices.GetRequiredService<GatewayRuntimeSettingsProvider>().Current.Version;

    // RemoteIpAddress, deliberately — and NOT X-Forwarded-For, which is the
    // opposite of what Auth_API does a few files away.
    //
    // The asymmetry is the point. The API sits BEHIND this process, so its TCP
    // peer is always the gateway and it must read the forwarded header instead
    // (see ClientIpResolver). This process is the EDGE: verified 2026-08-15 as
    // IIS + ANCM in-process hosting (web.config hostingModel="inprocess", which
    // hands Kestrel the real client address with no localhost hop), on Plesk for
    // Windows, which serves through IIS with no nginx layer, and with no CDN in
    // front — one origin A record, no via/x-cache/cf-ray on any response.
    //
    // So do NOT "fix" this by adding UseForwardedHeaders here. X-Forwarded-For
    // is client-supplied at the edge: trusting it would let any caller pick a
    // fresh rate-limit partition on every request and walk straight through
    // every limit below. That is strictly worse than the hazard it looks like
    // it is solving.
    //
    // If a CDN or another reverse proxy is ever put in front of this process,
    // every client collapses into one bucket and nothing here will notice.
    // Handle that by enabling forwarded headers WITH KnownProxies pinned to
    // that hop — never unconditionally.
    static string ClientId(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // Global rate limit
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var limits = Limits(context);

        return RateLimitPartition.GetFixedWindowLimiter(
            $"v{SettingsVersion(context)}:{ClientId(context)}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.GlobalPermitLimit,
                Window = TimeSpan.FromSeconds(limits.GlobalWindowSeconds),
                QueueLimit = limits.GlobalQueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });

    // Per-endpoint rate limits
    options.AddPolicy("auth", context =>
    {
        var limits = Limits(context);

        return RateLimitPartition.GetFixedWindowLimiter(
            $"v{SettingsVersion(context)}:{ClientId(context)}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.AuthPermitLimit,
                Window = TimeSpan.FromSeconds(limits.AuthWindowSeconds),
                QueueLimit = 0
            });
    });

    options.AddPolicy("api", context =>
    {
        var limits = Limits(context);

        return RateLimitPartition.GetFixedWindowLimiter(
            $"v{SettingsVersion(context)}:{ClientId(context)}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.ApiPermitLimit,
                Window = TimeSpan.FromSeconds(limits.ApiWindowSeconds),
                QueueLimit = 10
            });
    });

    options.AddPolicy("admin", context =>
    {
        var limits = Limits(context);

        return RateLimitPartition.GetFixedWindowLimiter(
            $"v{SettingsVersion(context)}:{ClientId(context)}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.AdminPermitLimit,
                Window = TimeSpan.FromSeconds(limits.AdminWindowSeconds),
                QueueLimit = 0
            });
    });

    options.OnRejected = async (context, token) =>
    {
        var localizer = context.HttpContext.RequestServices
            .GetService<Microsoft.Extensions.Localization.IStringLocalizer<Auth_Localization.Resources.Middleware.MiddlewareMessages>>();

        string Localize(string key, string fallback)
        {
            if (localizer is null) return fallback;
            var localized = localizer[key];
            return localized.ResourceNotFound ? fallback : localized.Value;
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
            ? (int)retry.TotalSeconds
            : 60;

        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.com/429",
            title = Localize("Middleware.TooManyRequests.Title", "Too Many Requests"),
            status = 429,
            detail = Localize("Middleware.TooManyRequests", "Rate limit exceeded. Please try again later."),
            retryAfter
        }, token);
    };
});

// Localization
builder.Services.AddAuthLocalization();

// Health Checks
//   /health -> liveness  (tag "live") : is the gateway process up? No upstream call.
//   /ready  -> readiness (tag "ready"): can the gateway reach a READY Auth API (DB + signing key)?
// The readiness probe targets the Auth API's /ready endpoint. Resolve it DEFENSIVELY so a missing or
// invalid value (e.g. an unreplaced "{{AUTH_API_INTERNAL_URL}}" placeholder) can never crash startup
// with a UriFormatException (which surfaces as an opaque IIS HTTP 500.30):
//   1) an explicit, well-formed Services:AuthApi:ReadyUrl, else
//   2) derive it from a well-formed Services:AuthApi:BaseUrl, else
//   3) fall back to localhost (the readiness check then fails at runtime instead of stopping the app).
static bool TryAbsoluteHttpUri(string? value) =>
    Uri.TryCreate(value, UriKind.Absolute, out var uri)
    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

var configuredReadyUrl = builder.Configuration["Services:AuthApi:ReadyUrl"];
var configuredBaseUrl = builder.Configuration["Services:AuthApi:BaseUrl"];

string authApiReadyUrl;
if (TryAbsoluteHttpUri(configuredReadyUrl))
{
    authApiReadyUrl = configuredReadyUrl!;
}
else if (TryAbsoluteHttpUri(configuredBaseUrl))
{
    authApiReadyUrl = configuredBaseUrl!.TrimEnd('/') + "/ready";
}
else
{
    authApiReadyUrl = "http://localhost:5100/ready";
    Log.Warning(
        "Services:AuthApi:ReadyUrl/BaseUrl are missing or not valid absolute http(s) URLs " +
        "(ReadyUrl='{ReadyUrl}', BaseUrl='{BaseUrl}'). Using {Fallback} for the readiness probe; " +
        "set Services:AuthApi:BaseUrl in appsettings.Production.json.",
        configuredReadyUrl, configuredBaseUrl, authApiReadyUrl);
}

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("API Gateway process is running."), tags: ["live"])
    .AddUrlGroup(new Uri(authApiReadyUrl), name: "auth-api", tags: ["ready"]);

// System settings shared with the Auth API (CORS origins, health-error detail).
// This process has no database layer by design, so it pulls them from the API
// instead: see GatewayRuntimeSettingsPoller for why pull beats shared storage
// and push here. Until the first successful poll — and through any API outage —
// the values below are this process's own configuration file.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<GatewayRuntimeSettingsProvider>();
builder.Services.AddHostedService<GatewayRuntimeSettingsPoller>();

// CORS: the policy is rebuilt from those live values rather than frozen here,
// so origins saved in the console apply to the gateway without a restart.
builder.Services.AddCors();
builder.Services.AddSingleton<ICorsPolicyProvider, DynamicCorsPolicyProvider>();

var app = builder.Build();

// Request logging
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("ClientIP", httpContext.Connection.RemoteIpAddress?.ToString());
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());

        if (httpContext.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
        {
            diagnosticContext.Set("CorrelationId", correlationId.ToString());
        }
    };
});

// Localization middleware
app.UseAuthLocalization();

// Exception handling
app.UseMiddleware<GatewayExceptionMiddleware>();

// Security headers
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();

// Health endpoints (detailed JSON breakdown per check).
// Exception messages are included only in Development, or when HealthChecks:ExposeErrorDetails is
// explicitly enabled, because these endpoints are publicly reachable and could leak internal info.
var gatewayRuntimeSettings = app.Services.GetRequiredService<GatewayRuntimeSettingsProvider>();
var isDevelopment = app.Environment.IsDevelopment();

Task WriteHealthResponse(HttpContext httpContext, HealthReport report)
{
    // Read per request, not captured once: the console toggle must apply to the
    // gateway's own /health and /ready the same way it applies to the API's.
    var exposeHealthErrors = isDevelopment
        || gatewayRuntimeSettings.Current.HealthChecksExposeErrorDetails;

    httpContext.Response.ContentType = "application/json; charset=utf-8";
    return httpContext.Response.WriteAsync(HealthCheckJsonFormatter.Serialize(report, exposeHealthErrors));
}

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = WriteHealthResponse
});
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});

// Gateway info endpoint
app.MapGet("/", () => new
{
    name = "Auth System API Gateway",
    version = "1.0.0",
    status = "running"
}).ExcludeFromDescription();

// YARP reverse proxy
app.MapReverseProxy();

try
{
    Log.Information("Starting API Gateway...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "API Gateway terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
