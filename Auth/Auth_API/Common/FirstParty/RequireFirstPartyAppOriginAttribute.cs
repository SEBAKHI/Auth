using Auth.Domain.Errors;
using Auth_API.Common.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Auth_API.Common.FirstParty;

/// <summary>
/// Refuses, with 403 <c>Auth.FirstPartyOriginRequired</c>, every request whose Origin does not
/// name one of <c>IdentityProvider:FirstPartySpaOrigins</c> exactly: a request with no Origin
/// and every request while the list is empty included. For an endpoint that builds something
/// FROM the Origin (the Google code exchange builds the callback address it sends Google).
/// </summary>
/// <remarks>
/// Stricter than <see cref="RequireFirstPartyOriginAttribute"/> on its two leniencies, on
/// purpose. That filter lets a request without an Origin through (a server client, which
/// SameSite is not about) and refuses nothing while no app is listed (the behaviour before the
/// list existed). Neither leniency makes sense here: with no listed Origin there is no app to
/// build the address from, so the endpoint has nothing to do, and an exact match is what keeps
/// the address the server sends Google one of the apps' own.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireFirstPartyAppOriginAttribute() : TypeFilterAttribute(typeof(RequireFirstPartyAppOriginFilter));

/// <summary>
/// The Origin check behind <see cref="RequireFirstPartyAppOriginAttribute"/>. A resource filter,
/// so it refuses before the body is bound or validated.
/// </summary>
public sealed class RequireFirstPartyAppOriginFilter(
    IFirstPartyOriginResolver originResolver,
    ProblemDetailsFactory problemDetailsFactory) : IAsyncResourceFilter
{
    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        // No Origin, "null", http, unlisted, or an empty list: all resolve to no app.
        if (originResolver.Resolve(context.HttpContext.Request) is null)
        {
            context.Result = ProblemMapping.ToProblem(
                context.HttpContext,
                problemDetailsFactory,
                [AuthErrors.FirstPartyOriginRequired],
                bodyType: null);
            return;
        }

        await next();
    }
}
