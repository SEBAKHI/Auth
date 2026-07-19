using System.Threading.RateLimiting;
using API_Gateway.Middleware;
using Auth_Localization.Extensions;
using Auth.Shared.Configuration;
using Auth.Shared.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

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

var storageMode = AuthDataProtectionExtensions.ParseStorageMode(
    builder.Configuration["SecretManagement:StorageMode"]);
var dpCertificateSettings = builder.Configuration
    .GetSection(DataProtectionCertificateSettings.SectionName)
    .Get<DataProtectionCertificateSettings>() ?? new DataProtectionCertificateSettings();

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

// In production, gateway token must be configured (via DPAPI secrets file)
if (string.IsNullOrEmpty(gatewayToken) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Gateway token is not configured. In production, ensure the DPAPI secrets file " +
        "contains the GatewayToken. File location: " + SecretConfigurationExtensions.GetDefaultSecretFilePath());
}

// YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(builderContext =>
    {
        // Add gateway token to all proxied requests (only if configured)
        if (!string.IsNullOrEmpty(gatewayToken))
        {
            builderContext.AddRequestTransform(context =>
            {
                context.ProxyRequest.Headers.Add("X-Gateway-Token", gatewayToken);
                return ValueTask.CompletedTask;
            });
        }

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

    // Global rate limit
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var clientId = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(clientId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:GlobalPermitLimit", 1000),
            Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:GlobalWindowSeconds", 60)),
            QueueLimit = builder.Configuration.GetValue("RateLimiting:GlobalQueueLimit", 100),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });

    // Per-endpoint rate limits
    options.AddPolicy("auth", context =>
    {
        var clientId = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(clientId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 20),
            Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:AuthWindowSeconds", 60)),
            QueueLimit = 0
        });
    });

    options.AddPolicy("api", context =>
    {
        var clientId = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(clientId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:ApiPermitLimit", 100),
            Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:ApiWindowSeconds", 60)),
            QueueLimit = 10
        });
    });

    options.AddPolicy("admin", context =>
    {
        var clientId = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(clientId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:AdminPermitLimit", 10),
            Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:AdminWindowSeconds", 60)),
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

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["*"];

        if (origins.Contains("*"))
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins(origins);

            // The IdP session cookie rides on credentialed requests from the
            // accounts SPA. AllowCredentials is only legal with explicit
            // origins, never with AllowAnyOrigin.
            if (builder.Configuration.GetValue("Cors:AllowCredentials", false))
            {
                policy.AllowCredentials();
            }
        }

        policy.AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("X-Correlation-ID", "X-RateLimit-Remaining", "Retry-After");
    });
});

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
var exposeHealthErrors = app.Environment.IsDevelopment()
    || app.Configuration.GetValue("HealthChecks:ExposeErrorDetails", false);

Task WriteHealthResponse(HttpContext httpContext, HealthReport report)
{
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
