using Auth.Domain.Errors;
using Auth_API.Common.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace Auth_API.Common.FirstParty;

/// <summary>
/// Refuses, with 403 <c>Auth.FirstPartyOriginRequired</c>, a browser request whose
/// Origin is not one of <c>IdentityProvider:FirstPartySpaOrigins</c>. On every
/// sign-in exit but the refresh, on the SSO sign-out confirmation, and on the
/// cookie sign-out.
/// </summary>
/// <remarks>
/// This is the same-site CSRF barrier. SameSite cookies cannot tell the console
/// from the apex site or a sibling subdomain — they are all the same site — so a
/// page there could otherwise sign a victim into its own account (login CSRF
/// against <c>auth_idp</c>, through any sign-in exit) or end their SSO session.
/// The Origin header is set by the browser and cannot be chosen by the page, so
/// an exact match on it is.
/// <para>
/// Two rules keep everyone else working. While the list is empty nothing is
/// refused: no first-party app is known, which is the behaviour before the list
/// existed, and the API logs a warning at boot. And a request with NO Origin
/// passes: browsers send one on every POST, so its absence means a server client
/// or a tool, which SameSite and this barrier are not about. <c>"null"</c> is an
/// Origin, and is refused.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireFirstPartyOriginAttribute() : TypeFilterAttribute(typeof(RequireFirstPartyOriginFilter));

/// <summary>
/// The Origin check behind <see cref="RequireFirstPartyOriginAttribute"/>. A resource
/// filter, so it refuses before the body is bound or validated.
/// </summary>
public sealed class RequireFirstPartyOriginFilter(
    IFirstPartyOriginResolver originResolver,
    ProblemDetailsFactory problemDetailsFactory) : IAsyncResourceFilter
{
    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (!Allows(context.HttpContext.Request))
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

    private bool Allows(HttpRequest request) =>
        !originResolver.HasOrigins ||
        !request.Headers.ContainsKey(HeaderNames.Origin) ||
        // "null", http, an unlisted or a same-site sibling origin all resolve to no app.
        originResolver.Resolve(request) is not null;
}
