using Auth.Sdk.Authorization;
using Auth.Sdk.Handlers;
using Auth.Sdk.TokenManagement;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Sdk.Extensions;

/// <summary>
/// Extension methods for registering AuthSystem authentication in a consuming application.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>The machine token request's whole budget: one attempt, no retry.</summary>
    public static readonly TimeSpan MachineTokenRequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Adds AuthSystem authentication with three schemes: Bearer (JWT), ApiKey, and WebhookKey.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action for AuthSystem options.</param>
    /// <returns>The authentication builder for further customization.</returns>
    public static AuthenticationBuilder AddAuthSystemAuthentication(
        this IServiceCollection services,
        Action<AuthSystemOptions> configure)
    {
        var options = new AuthSystemOptions();
        configure(options);

        // Every application's machine tokens share one audience (OI-128). An application that took
        // it as its own would accept them all as callers; refused here, before anything is served.
        if (string.Equals(
                options.Audience?.Trim().TrimEnd('/'),
                AuthSystemConstants.MachineTokenAudience,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"AuthSystemOptions.Audience is the machine token audience '{AuthSystemConstants.MachineTokenAudience}'. "
                + "Set it to this application's own client id.");
        }

        services.Configure(configure);

        // Register the AuthSystemClient
        services.AddSingleton<AuthSystemClient>();

        // Register permission-based authorization.
        //
        // Two handlers, because there are two genuinely different questions.
        // [RequirePermission] asks about application-wide authority and reads the
        // flat "permissions" claim. [RequireOrganizationPermission] asks whether
        // the caller holds it IN THE ORGANIZATION THIS REQUEST NAMES, and reads
        // the organization-tagged "org_perm" claims. Using the first where the
        // second belongs is how a permission granted inside one organization gets
        // spent on another's data.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionRequirementHandler>();
        services.AddScoped<IAuthorizationHandler, OrganizationPermissionRequirementHandler>();

        // The organization handler resolves the target organization from the
        // route, so it needs the request.
        services.AddHttpContextAccessor();

        // Register memory cache for validation result caching
        services.AddMemoryCache();

        // Register token management (auto-refresh interceptor)
        services.AddSingleton<ITokenStore, InMemoryTokenStore>();
        services.AddTransient<TokenRefreshHandler>();

        // Register named HTTP client for AuthSystem API calls.
        //
        // The SOLE place the gateway token is attached. AuthSystemClient used to
        // add it a second time when resolving the client, and because the
        // factory re-runs this delegate on every CreateClient the two adds
        // produced a two-value header the API could never match.
        //
        // Resolved from IOptions rather than the captured local so a consumer
        // that reconfigures AuthSystemOptions after registration is honoured,
        // and skipped entirely when no token is configured — an empty header is
        // not the same as no header to a validating gateway.
        services.AddHttpClient(AuthSystemConstants.HttpClientName, (sp, client) =>
        {
            var current = sp.GetRequiredService<IOptions<AuthSystemOptions>>().Value;

            client.BaseAddress = new Uri(current.BaseUrl);
            if (!string.IsNullOrWhiteSpace(current.GatewayToken))
            {
                client.DefaultRequestHeaders.Add(
                    AuthSystemConstants.GatewayTokenHeaderName,
                    current.GatewayToken);
            }
        })
        .AddHttpMessageHandler<TokenRefreshHandler>();

        // The machine client's own named client (OI-128): the same base address and gateway
        // token, and NO TokenRefreshHandler. That handler attaches the stored user token to every
        // request; the machine token request authenticates with its signed assertion alone, and a
        // user's bearer token must never ride along on it.
        services.AddHttpClient(AuthSystemConstants.MachineHttpClientName, (sp, client) =>
        {
            var current = sp.GetRequiredService<IOptions<AuthSystemOptions>>().Value;

            client.BaseAddress = new Uri(current.BaseUrl);
            client.Timeout = MachineTokenRequestTimeout;
            if (!string.IsNullOrWhiteSpace(current.GatewayToken))
            {
                client.DefaultRequestHeaders.Add(
                    AuthSystemConstants.GatewayTokenHeaderName,
                    current.GatewayToken);
            }
        })
        // A 307 or 308 would re-post the assertion and the gateway token to wherever it points.
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        // One provider per process, so its cached token and its single-flight gate are shared.
        // Registered whether or not the machine options are set: it reports a missing option
        // when it is first asked for a token, not at startup.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMachineTokenProvider, MachineTokenProvider>();

        // Configure authentication with three schemes
        var authBuilder = services.AddAuthentication(authOptions =>
        {
            authOptions.DefaultAuthenticateScheme = AuthSystemConstants.BearerScheme;
            authOptions.DefaultChallengeScheme = AuthSystemConstants.BearerScheme;
        });

        // Scheme 1: JWT Bearer via JWKS from AuthSystem
        authBuilder.AddJwtBearer(AuthSystemConstants.BearerScheme, jwtOptions =>
        {
            jwtOptions.Authority = options.BaseUrl;
            jwtOptions.MetadataAddress = $"{options.BaseUrl.TrimEnd('/')}/.well-known/openid-configuration";
            jwtOptions.RequireHttpsMetadata = !options.BaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase);

            jwtOptions.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = options.Issuer,
                ValidateAudience = true,
                ValidAudience = options.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                // Pin the signing algorithm to RS256 so a token cannot be
                // accepted under any other alg (defense-in-depth against
                // algorithm-substitution; the JWKS only ever carries RSA keys).
                ValidAlgorithms = ["RS256"],
                ClockSkew = TimeSpan.FromSeconds(30),
                RoleClaimType = "roles",
                NameClaimType = "sub"
            };
        });

        // Scheme 2: API Key via X-Api-Key header
        authBuilder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            AuthSystemConstants.ApiKeyScheme, _ => { });

        // Scheme 3: Webhook Key via ?whk= query parameter
        authBuilder.AddScheme<WebhookKeyAuthenticationOptions, WebhookKeyAuthenticationHandler>(
            AuthSystemConstants.WebhookKeyScheme, _ => { });

        return authBuilder;
    }
}
