using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Auth.Shared.Http.ErrorContract;

/// <summary>
/// Wires the error contract of ADR 0001 into a host: one problem-details writer, one exception
/// handler, and problem bodies for the framework's empty error responses.
/// </summary>
public static class ErrorContractExtensions
{
    /// <summary>
    /// Registers the problem-details customization, <see cref="ErrorContractExceptionHandler"/> and
    /// <see cref="OutageOptions"/>. A host adds its <see cref="IExceptionProblemTranslator"/>s itself.
    /// </summary>
    public static IServiceCollection AddErrorContract(this IServiceCollection services)
    {
        services.AddOptions<OutageOptions>()
            .BindConfiguration(OutageOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddLocalization();
        services.TryAddSingleton<ProblemText>();

        services.AddProblemDetails();
        services.AddOptions<ProblemDetailsOptions>()
            .Configure<ProblemText, IOptions<OutageOptions>>((options, text, outage) =>
                options.CustomizeProblemDetails = context => ProblemCustomization.Apply(context, text, outage.Value));

        services.AddExceptionHandler<ErrorContractExceptionHandler>();

        return services;
    }

    /// <summary>
    /// Adds the exception handler, then the status-code pages that give every empty 4xx/5xx a
    /// problem body. Call it after request localization, so <c>detail</c> is in the request's
    /// culture, and before authentication, rate limiting and routing, whose empty 401, 403, 429,
    /// 404 and 405 responses it has to see.
    /// </summary>
    public static IApplicationBuilder UseErrorContract(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        return app;
    }
}
