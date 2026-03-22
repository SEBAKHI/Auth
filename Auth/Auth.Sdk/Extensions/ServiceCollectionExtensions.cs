using Auth.Sdk.Authorization;
using Auth.Sdk.Handlers;
using Auth.Sdk.TokenManagement;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Sdk.Extensions;

/// <summary>
/// Extension methods for registering AuthSystem authentication in a consuming application.
/// </summary>
public static class ServiceCollectionExtensions
{
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

        services.Configure(configure);

        // Register the AuthSystemClient
        services.AddSingleton<AuthSystemClient>();

        // Register permission-based authorization
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionRequirementHandler>();

        // Register memory cache for validation result caching
        services.AddMemoryCache();

        // Register token management (auto-refresh interceptor)
        services.AddSingleton<ITokenStore, InMemoryTokenStore>();
        services.AddTransient<TokenRefreshHandler>();

        // Register named HTTP client for AuthSystem API calls
        services.AddHttpClient(AuthSystemConstants.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.Add(
                AuthSystemConstants.GatewayTokenHeaderName,
                options.GatewayToken);
        })
        .AddHttpMessageHandler<TokenRefreshHandler>();

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
