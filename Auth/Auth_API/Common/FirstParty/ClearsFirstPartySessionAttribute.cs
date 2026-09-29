using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Auth_API.Common.FirstParty;

/// <summary>
/// Marks the sign-out: its response expires the refresh cookie of the first-party
/// app the request came from. The tokens behind it are revoked by the handler;
/// this only stops the browser from presenting a dead cookie.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ClearsFirstPartySessionAttribute() : TypeFilterAttribute(typeof(ClearFirstPartySessionResultFilter));

/// <summary>
/// Expires the requesting app's refresh cookie on the way out.
/// </summary>
public sealed class ClearFirstPartySessionResultFilter(IFirstPartyOriginResolver originResolver) : IAsyncResultFilter
{
    /// <inheritdoc />
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (originResolver.Resolve(context.HttpContext.Request) is { } app)
        {
            FirstPartyRefreshCookie.Delete(context.HttpContext.Response, app);
        }

        await next();
    }
}
