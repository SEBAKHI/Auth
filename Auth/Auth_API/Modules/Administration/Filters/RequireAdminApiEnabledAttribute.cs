using Auth.Application.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Administration.Filters;

/// <summary>
/// Action filter that ensures the Admin API is enabled before executing the action.
/// Returns 403 Forbidden if SecretManagement:EnableAdminApi is false.
/// Apply at controller or action level to enforce this requirement.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireAdminApiEnabledAttribute : Attribute, IAsyncActionFilter
{
    /// <summary>
    /// Executes before the action method, checking if Admin API is enabled.
    /// </summary>
    /// <param name="context">The action executing context.</param>
    /// <param name="next">The delegate to execute the next filter or action.</param>
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var settings = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<SecretManagementSettings>>().Value;

        if (!settings.EnableAdminApi)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Title = "Admin API Disabled",
                Detail = "Secret management admin API is disabled.",
                Status = StatusCodes.Status403Forbidden
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
