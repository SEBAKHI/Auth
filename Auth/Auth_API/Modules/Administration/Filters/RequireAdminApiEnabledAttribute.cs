using Auth.Application.Configuration;
using Auth.Domain.Errors;
using Auth_API.Common.Errors;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Administration.Filters;

/// <summary>
/// Action filter that ensures the Admin API is enabled before executing the action.
/// Returns 403 Forbidden with Secret.AdminApiDisabled if SecretManagement:EnableAdminApi is false.
/// Apply at controller or action level to enforce this requirement.
/// </summary>
/// <remarks>
/// The switch is read on every request (AM-S11-1), through a snapshot bound when the
/// request's scope asks for it, so a settings-file edit takes effect on the next request
/// with no restart. Never a monitor: when a reload of a settings file fails to parse, the
/// file's values are dropped before the monitor is told, so a monitor would keep serving
/// the last "true" (open), while a fresh snapshot reads the remaining sources, whose
/// default is "false" (closed).
/// </remarks>
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
            .GetRequiredService<IOptionsSnapshot<SecretManagementSettings>>().Value;

        if (!settings.EnableAdminApi)
        {
            context.Result = ProblemMapping.ToProblem(
                context.HttpContext,
                context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>(),
                [SecretErrors.AdminApiDisabled],
                bodyType: null);
            return;
        }

        await next();
    }
}
