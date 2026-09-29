using Auth.Application.Configuration;
using Auth.Domain.Errors;
using Auth_API.Common.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Auth_API.Common.FirstParty;

/// <summary>
/// Refuses, with 403 <c>Auth.FirstPartyOriginRequired</c>, a browser request whose
/// Origin is not one of <c>IdentityProvider:FirstPartySpaOrigins</c>.
/// </summary>
/// <remarks>
/// This is the same-site CSRF barrier. SameSite cookies cannot tell the console
/// from the apex site or a sibling subdomain — they are all the same site — so a
/// page there could otherwise sign a victim into its own account (password login
/// CSRF against <c>auth_idp</c>) or end their SSO session. The Origin header is set
/// by the browser and cannot be chosen by the page, so an exact match on it is.
/// <para>
/// While the list is empty nothing is refused: no first-party app is known, which
/// is the behaviour before the list existed. The API logs a warning at boot.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireFirstPartyOriginAttribute : TypeFilterAttribute
{
    /// <param name="onlyWithRefreshCookie">
    /// <c>false</c> (password sign-in): always enforced once the list is filled, and a
    /// request with NO Origin passes — browsers send one on every POST, so its absence
    /// means a server client or a tool, which SameSite cannot touch anyway.
    /// <c>true</c> (the SSO logout confirmation): enforced only while the refresh
    /// cookie delivery is on, and then a missing Origin is refused too.
    /// </param>
    public RequireFirstPartyOriginAttribute(bool onlyWithRefreshCookie = false)
        : base(typeof(RequireFirstPartyOriginFilter))
    {
        Arguments = [onlyWithRefreshCookie];
    }
}

/// <summary>
/// The Origin check behind <see cref="RequireFirstPartyOriginAttribute"/>. A resource
/// filter, so it refuses before the body is bound or validated.
/// </summary>
public sealed class RequireFirstPartyOriginFilter(
    bool onlyWithRefreshCookie,
    IFirstPartyOriginResolver originResolver,
    IOptionsSnapshot<IdentityProviderSettings> idpSettings,
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

    private bool Allows(HttpRequest request)
    {
        if (!originResolver.HasOrigins)
        {
            return true;
        }

        if (onlyWithRefreshCookie && !idpSettings.Value.SpaRefreshCookieEnabled)
        {
            return true;
        }

        var hasOrigin = request.Headers.ContainsKey(HeaderNames.Origin);
        if (!hasOrigin)
        {
            return !onlyWithRefreshCookie;
        }

        // "null", http, an unlisted or a same-site sibling origin all resolve to no app.
        return originResolver.Resolve(request) is not null;
    }
}
