using System.Collections.Frozen;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Shared.Http.ErrorContract;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Auth_API.Common.FirstParty;

/// <summary>
/// Marks an action that hands a browser a session — every sign-in exit and the
/// refresh. Its result passes through <see cref="FirstPartySessionResultFilter"/>,
/// the one place that writes the session's cookies.
/// FirstPartySignInExitCoverageTests fails when an action that returns a
/// <see cref="LoginResponse"/> or a <see cref="TokenResponse"/> lacks it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IssuesFirstPartySessionAttribute() : TypeFilterAttribute(typeof(FirstPartySessionResultFilter));

/// <summary>
/// Delivers a session's credentials. The handlers only return tokens; how they
/// reach the browser is decided here, at the HTTP boundary, per request:
/// <list type="number">
/// <item>The SSO session token always goes into the <c>auth_idp</c> cookie.</item>
/// <item>Cookie delivery on and the Origin a listed first-party app: the refresh
/// token goes into that app's HttpOnly cookie, and the body carries
/// <see cref="FirstPartyRefreshCookie.Sentinel"/> in its place.</item>
/// <item>Cookie delivery off but the token came from the cookie (the switch was
/// just turned off): the body carries the real token and the cookie is expired in
/// the same response. That is the rollback, and it signs no one out.</item>
/// <item>Anything else — a server client, Postman, an unlisted origin: the body is
/// untouched.</item>
/// </list>
/// A final refusal of a token that came from the cookie expires the cookie.
/// </summary>
public sealed class FirstPartySessionResultFilter(
    IFirstPartyOriginResolver originResolver,
    IOptionsSnapshot<IdentityProviderSettings> idpSettings,
    IConfiguration configuration,
    ILogger<FirstPartySessionResultFilter> logger) : IAsyncResultFilter
{
    /// <summary>
    /// The refusals after which the presented refresh token can never work again.
    /// The same list the browser client keeps (<c>FINAL_REFRESH_REJECTIONS</c> in
    /// Auth_UI/packages/api/src/client.ts): on those, it forgets the session; here,
    /// the cookie that carried the dead token goes with it. Anything else — a rate
    /// limit, an outage, an inactive application — keeps the cookie.
    /// </summary>
    private static readonly FrozenSet<string> FinalRefreshRejections = new[]
    {
        "Auth.TokenRevoked",
        "Auth.RefreshTokenRevoked",
        "Auth.RefreshTokenNotFound",
        "Auth.RefreshTokenExpired",
        "User.NotFound",
        "User.AccountLocked",
        "User.AccountLockedUntil",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var credential = FirstPartySessionContext.GetCredential(httpContext);

        if (context.Result is ObjectResult { Value: ISessionIssuingResponse issued } result &&
            (result.StatusCode ?? StatusCodes.Status200OK) is >= 200 and < 300)
        {
            Deliver(httpContext, result, issued, credential);
        }
        else if (credential is { Channel: RefreshCredentialChannel.Cookie, App: { } app } &&
                 httpContext.Items.TryGetValue(ProblemItems.Code, out var code) &&
                 code is string problemCode &&
                 FinalRefreshRejections.Contains(problemCode))
        {
            FirstPartyRefreshCookie.Delete(httpContext.Response, app);
        }

        await next();
    }

    private void Deliver(
        HttpContext httpContext,
        ObjectResult result,
        ISessionIssuingResponse issued,
        RefreshCredential? credential)
    {
        var settings = idpSettings.Value;
        IdpSessionCookie.Apply(httpContext.Response, issued.IssuedIdpSessionToken, settings);

        // A sign-in waiting on its second factor issues nothing yet.
        if (issued.IssuedTokens is not { } tokens)
        {
            return;
        }

        var app = credential?.App ?? originResolver.Resolve(httpContext.Request);

        if (settings.SpaRefreshCookieEnabled && app is not null)
        {
            FirstPartyRefreshCookie.Apply(httpContext.Response, app, tokens.RefreshToken, tokens.RefreshExpiresIn);
            result.Value = issued.WithRefreshToken(FirstPartyRefreshCookie.Sentinel);

            if (credential is { Channel: RefreshCredentialChannel.Body })
            {
                logger.LogWarning(
                    "SpaRefresh.LegacyBodyMigrated: a refresh token stored by a first-party app moved from the body to its cookie. Origin: {Origin}",
                    app.Origin);
            }

            return;
        }

        if (credential is { Channel: RefreshCredentialChannel.Cookie, App: { } cookieApp })
        {
            // Cookie delivery was switched off: the real token goes back to the body
            // (it already is there) and the cookie that carried it is expired.
            FirstPartyRefreshCookie.Delete(httpContext.Response, cookieApp);
            return;
        }

        if (app is null && IsCorsOrigin(httpContext.Request.Headers[HeaderNames.Origin].ToString()))
        {
            // A browser page that may read this response and is not one of the
            // platform's apps received a refresh token in the body. Counted, so the
            // optional follow-up that closes this can be scheduled when it is zero.
            logger.LogWarning(
                "SpaRefresh.BodyTokenToCorsOrigin: a refresh token was returned in the body to a CORS origin that is not first-party. Origin: {Origin}",
                httpContext.Request.Headers[HeaderNames.Origin].ToString());
        }
    }

    private bool IsCorsOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        var normalized = origin.Trim().TrimEnd('/');
        return configuration.GetSection("Cors:AllowedOrigins").GetChildren()
            .Select(child => child.Value?.Trim().TrimEnd('/'))
            .Any(allowed => string.Equals(allowed, normalized, StringComparison.OrdinalIgnoreCase));
    }
}
