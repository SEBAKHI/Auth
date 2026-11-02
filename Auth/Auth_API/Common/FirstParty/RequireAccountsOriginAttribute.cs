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
/// Refuses, with 403 <c>Auth.FirstPartyOriginRequired</c>, every request whose Origin is not
/// exactly the accounts app's (<c>IdentityProvider:AccountsBaseUrl</c>). On the account bar of
/// linked applications, which only the accounts app's bar pages read.
/// </summary>
/// <remarks>
/// Narrower than <see cref="RequireFirstPartyOriginAttribute"/> on purpose, in three ways. The
/// endpoint answers who is signed in, by the single sign-on cookie alone, and a linked
/// application on a sibling subdomain is same-site with the API, so SameSite lets its page
/// send that cookie; only the Origin tells the accounts app from it.
/// <list type="bullet">
/// <item>One origin, not the first-party list: the console has no reason to read it.</item>
/// <item>A request with no Origin is refused too. Browsers send none on a plain GET that is
/// not CORS (an image, a script tag), and that is exactly the request a page on a sibling
/// subdomain could make with the cookie; a server client has no cookie to send.</item>
/// <item>With no accounts address configured nothing is allowed, rather than everything: a
/// deployment without the accounts app has no account bar.</item>
/// </list>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireAccountsOriginAttribute() : TypeFilterAttribute(typeof(RequireAccountsOriginFilter));

/// <summary>
/// The Origin check behind <see cref="RequireAccountsOriginAttribute"/>. A resource filter, so
/// it refuses before anything is bound.
/// </summary>
public sealed class RequireAccountsOriginFilter(
    IOptionsSnapshot<IdentityProviderSettings> settings,
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
        var accounts = OriginOf(settings.Value.AccountsBaseUrl);
        var origin = request.Headers[HeaderNames.Origin].ToString().Trim().TrimEnd('/').ToLowerInvariant();

        // https only: an http page could never have been framed by an https application,
        // and "null" (an opaque origin) is not an origin at all.
        return accounts is not null &&
               accounts.StartsWith("https://", StringComparison.Ordinal) &&
               string.Equals(origin, accounts, StringComparison.Ordinal);
    }

    private static string? OriginOf(string? address) =>
        Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant()
            : null;
}
