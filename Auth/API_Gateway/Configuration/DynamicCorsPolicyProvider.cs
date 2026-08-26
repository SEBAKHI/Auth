using Microsoft.AspNetCore.Cors.Infrastructure;

namespace API_Gateway.Configuration;

/// <summary>
/// Rebuilds the default CORS policy from the settings this process last read
/// from the Auth API, so origins saved in the system-settings console apply to
/// the gateway — the host browsers actually talk to — without a restart.
/// <para>
/// Mirrors the Auth API's provider of the same name, including its safety
/// property: a runtime state with no origins yields a deny-all policy rather
/// than an open one.
/// </para>
/// </summary>
public sealed class DynamicCorsPolicyProvider : ICorsPolicyProvider
{
    // Content-Disposition carries the name the API chose for a download, and
    // that name now says what the file was narrowed by and whether it is whole.
    // Unexposed, the browser hides it and the console has to invent a name — so
    // a one-person, one-role extract landed on disk called "audit-logs.csv".
    private static readonly string[] ExposedHeaders =
        ["X-Correlation-ID", "X-RateLimit-Remaining", "Retry-After", "Content-Disposition"];

    private readonly GatewayRuntimeSettingsProvider _settings;
    private readonly object _sync = new();
    private (string Fingerprint, CorsPolicy Policy)? _cache;

    public DynamicCorsPolicyProvider(GatewayRuntimeSettingsProvider settings)
        => _settings = settings;

    public Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var current = _settings.Current;
        var origins = current.CorsAllowedOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .ToArray();

        var fingerprint = string.Join('|', origins) + "|" + current.CorsAllowCredentials;

        // Locked, unlike a bare field read/write pair: request threads race here,
        // and a torn read that paired a new fingerprint with an old policy would
        // stick — every later request would match the fingerprint and keep
        // serving the stale policy forever.
        lock (_sync)
        {
            if (_cache is { } cached && cached.Fingerprint == fingerprint)
            {
                return Task.FromResult<CorsPolicy?>(cached.Policy);
            }

            var builder = new CorsPolicyBuilder();

            if (origins.Contains("*"))
            {
                builder.AllowAnyOrigin();
            }
            else if (origins.Length > 0)
            {
                builder.WithOrigins(origins);

                // The IdP session cookie rides on credentialed requests from the
                // accounts SPA. AllowCredentials is only legal with explicit
                // origins, never with AllowAnyOrigin.
                if (current.CorsAllowCredentials)
                {
                    builder.AllowCredentials();
                }
            }

            builder.AllowAnyMethod()
                   .AllowAnyHeader()
                   .WithExposedHeaders(ExposedHeaders);

            var policy = builder.Build();
            _cache = (fingerprint, policy);
            return Task.FromResult<CorsPolicy?>(policy);
        }
    }
}
