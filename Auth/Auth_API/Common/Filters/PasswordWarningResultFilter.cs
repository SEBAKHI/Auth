using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Auth_API.Common.Filters;

/// <summary>
/// Surfaces non-blocking password warnings recorded during the request (e.g. a breached password
/// accepted under <c>BreachAction.Warn</c>) as an <c>X-Password-Warning</c> response header.
/// Works uniformly for endpoints that return a body and for 204 No Content responses, without
/// changing any handler return type. A machine-readable code list is also emitted as
/// <c>X-Password-Warning-Code</c>.
/// </summary>
public sealed class PasswordWarningResultFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var warningContext = context.HttpContext.RequestServices.GetService(typeof(IPasswordWarningContext)) as IPasswordWarningContext;

        if (warningContext is { Warnings.Count: > 0 })
        {
            context.HttpContext.Response.Headers["X-Password-Warning"] =
                string.Join(" | ", warningContext.Warnings.Select(w => w.Message));
            context.HttpContext.Response.Headers["X-Password-Warning-Code"] =
                string.Join(" ", warningContext.Warnings.Select(w => w.Code));
        }

        await next();
    }
}
