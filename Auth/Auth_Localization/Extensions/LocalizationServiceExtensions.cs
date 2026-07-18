using System.Globalization;
using Auth_Localization.Resources;
using Auth_Localization.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Auth_Localization.Extensions;

/// <summary>
/// Extension methods for configuring localization services.
/// </summary>
public static class LocalizationServiceExtensions
{
    /// <summary>
    /// Supported cultures for the authentication system.
    /// </summary>
    public static readonly string[] SupportedCultures = ["en", "ar", "tr", "fr", "zh", "ur", "fa"];

    /// <summary>
    /// Adds authentication localization services to the service collection.
    /// </summary>
    public static IServiceCollection AddAuthLocalization(this IServiceCollection services)
    {
        // No ResourcesPath: the marker classes' namespaces already mirror the Resources/
        // folder (e.g. Auth_Localization.Resources.Errors.DomainErrors), so the type's
        // full name matches the embedded manifest name exactly. Setting ResourcesPath
        // here would double the "Resources" segment and make every lookup miss.
        services.AddLocalization();

        services.AddScoped<AuthLocalizer>();

        return services;
    }

    /// <summary>
    /// Configures the request localization middleware.
    /// </summary>
    public static IApplicationBuilder UseAuthLocalization(this IApplicationBuilder app)
    {
        var supportedCultures = SupportedCultures
            .Select(c => new CultureInfo(c))
            .ToArray();

        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture("en"),
            SupportedCultures = supportedCultures,
            SupportedUICultures = supportedCultures,
            FallBackToParentCultures = true,
            FallBackToParentUICultures = true
        };

        // Request culture providers (in order of priority)
        options.RequestCultureProviders = new List<IRequestCultureProvider>
        {
            // 1. Check query string (?culture=ar)
            new QueryStringRequestCultureProvider(),

            // 2. Check cookie
            new CookieRequestCultureProvider(),

            // 3. Check Accept-Language header
            new AcceptLanguageHeaderRequestCultureProvider(),

            // 4. Check custom header (X-Language)
            new CustomHeaderRequestCultureProvider()
        };

        return app.UseRequestLocalization(options);
    }
}

/// <summary>
/// Custom request culture provider that reads from X-Language header.
/// </summary>
public class CustomHeaderRequestCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var languageHeader = httpContext.Request.Headers["X-Language"].FirstOrDefault();

        if (string.IsNullOrEmpty(languageHeader))
        {
            return NullProviderCultureResult;
        }

        // Validate the language is supported
        if (!LocalizationServiceExtensions.SupportedCultures.Contains(languageHeader, StringComparer.OrdinalIgnoreCase))
        {
            return NullProviderCultureResult;
        }

        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(languageHeader));
    }
}
