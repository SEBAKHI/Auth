using System.Text.Json;
using Auth.Shared.Http.ErrorContract;
using Auth_Localization.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// The services a controller's error path resolves when a test calls the controller directly:
/// MVC's <c>ProblemDetailsFactory</c> and JSON options (camelCase, as in Program.cs), and the
/// error contract that writes <c>code</c> and <c>detail</c>.
/// </summary>
internal static class ControllerServices
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        services.AddAuthLocalization();
        services.AddErrorContract();

        return services.BuildServiceProvider();
    }
}
