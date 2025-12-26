using System.Threading.RateLimiting;
using API_Gateway.Middleware;
using Microsoft.AspNetCore.RateLimiting;
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

// Gateway Configuration
var gatewayToken = builder.Configuration["Gateway:Token"]
    ?? throw new InvalidOperationException("Gateway token is not configured");

// YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(builderContext =>
    {
        // Add gateway token to all proxied requests
        builderContext.AddRequestTransform(context =>
        {
            context.ProxyRequest.Headers.Add("X-Gateway-Token", gatewayToken);
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

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
            ? (int)retry.TotalSeconds
            : 60;

        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.com/429",
            title = "Too Many Requests",
            status = 429,
            detail = "Rate limit exceeded. Please try again later.",
            retryAfter
        }, token);
    };
});

// Health Checks
var authApiUrl = builder.Configuration["Services:AuthApi:HealthUrl"] ?? "http://localhost:5100/health";
builder.Services.AddHealthChecks()
    .AddUrlGroup(new Uri(authApiUrl), name: "auth-api", tags: ["ready"]);

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

// Exception handling
app.UseMiddleware<GatewayExceptionMiddleware>();

// Security headers
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();

// Health endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
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
