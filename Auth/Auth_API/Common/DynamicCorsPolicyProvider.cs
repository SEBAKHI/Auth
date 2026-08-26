using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Auth_API.Common;

/// <summary>
/// CORS policy provider that rebuilds the default policy from the LIVE
/// configuration (files + database overrides), so saved origin changes apply
/// without a restart. The built policy is cached until the underlying values
/// change; production still fails fast at startup when no origins are
/// configured (see Program.cs), and a runtime state with no origins yields a
/// deny-all policy rather than an open one.
/// </summary>
public sealed class DynamicCorsPolicyProvider : ICorsPolicyProvider
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private (string Fingerprint, CorsPolicy Policy)? _cache;

    public DynamicCorsPolicyProvider(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        // Empty entries are array-shrink tombstones from the database layer.
        var origins = _configuration.GetSection("Cors:AllowedOrigins").GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
        var allowCredentials = _configuration.GetValue("Cors:AllowCredentials", false);

        var fingerprint = string.Join('|', origins) + "|" + allowCredentials;
        if (_cache is { } cached && cached.Fingerprint == fingerprint)
        {
            return Task.FromResult<CorsPolicy?>(cached.Policy);
        }

        var builder = new CorsPolicyBuilder();
        if (origins.Length > 0 && !origins.Contains("*"))
        {
            builder.WithOrigins(origins)
                .AllowAnyMethod()
                .AllowAnyHeader();

            if (allowCredentials)
            {
                builder.AllowCredentials();
            }
        }
        else if (_environment.IsDevelopment())
        {
            // Allow any origin in development only.
            builder.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        }

        // Content-Disposition carries the name the API chose for a download, and
        // that name says what an export was narrowed by and whether it is whole.
        // Unexposed, the browser hides it from script and the console has to
        // invent a name — which is how a one-person extract reached disk called
        // "audit-logs.csv". Harmless on a deny-all policy, which has no origins
        // to expose it to.
        builder.WithExposedHeaders("Content-Disposition");

        // No origins outside development: the policy stays empty (deny-all)
        // instead of throwing on a live request path.
        var policy = builder.Build();
        _cache = (fingerprint, policy);
        return Task.FromResult<CorsPolicy?>(policy);
    }
}
