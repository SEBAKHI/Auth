using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth_API.Common.Errors;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Common.Authentication;

/// <summary>
/// Registers the API's three bearer schemes from <see cref="AccessTokenValidation"/>, and the
/// revocation endpoint's <see cref="IIssuedAccessTokenValidator"/> from the platform and userinfo
/// profiles.
/// </summary>
/// <remarks>
/// The platform scheme is the default, so it is the only one <c>UseAuthentication</c> runs and
/// the only one a bare <c>[Authorize]</c> or <c>[RequirePermission]</c> uses. The userinfo and
/// machine schemes run only where an action names them. An application's token, or a machine
/// token, fails the default scheme on its audience, leaves the request anonymous, and so gets 401
/// everywhere except where its own scheme is named.
/// <para>
/// Machine tokens stay out of <see cref="IIssuedAccessTokenValidator"/>: they are not revocable one
/// by one. They live five minutes, and revoking the machine key stops new ones (OI-128).
/// </para>
/// </remarks>
public static class BearerSchemeRegistration
{
    public static AuthenticationBuilder AddAuthSystemBearerSchemes(
        this IServiceCollection services,
        JwtSettings settings,
        SecurityKey signingKey)
    {
        // The revocation endpoint's test of "a token this server signed": the same two profiles,
        // from the same arguments as the schemes below, so the two cannot drift apart.
        services.AddSingleton<IIssuedAccessTokenValidator>(new IssuedAccessTokenValidator(
            AccessTokenValidation.Platform(settings, signingKey),
            AccessTokenValidation.UserInfo(settings, signingKey)));

        // Introspection's test of "an access token issued to one application" (OI-128 PR B): the
        // userinfo scheme's profile, from the same arguments.
        services.AddSingleton<IApplicationAccessTokenValidator>(new ApplicationAccessTokenValidator(
            AccessTokenValidation.UserInfo(settings, signingKey)));

        return services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options => Configure(options, AccessTokenValidation.Platform(settings, signingKey)))
            .AddJwtBearer(
                AccessTokenValidation.UserInfoScheme,
                options => Configure(options, AccessTokenValidation.UserInfo(settings, signingKey)))
            .AddJwtBearer(
                AccessTokenValidation.MachineScheme,
                options => Configure(options, AccessTokenValidation.Machine(settings, signingKey)));
    }

    private static void Configure(JwtBearerOptions options, TokenValidationParameters parameters)
    {
        // Disable claim type mapping to preserve original JWT claim names
        options.MapInboundClaims = false;
        options.TokenValidationParameters = parameters;

        options.Events = new JwtBearerEvents
        {
            // No OnMessageReceived hook on purpose. Reading the token from a query string would
            // route it around JwtBlacklistValidationMiddleware, which keys on the Authorization
            // header and lets a request through untouched when that header is absent - so a
            // revoked, logged-out or locked-out token presented as ?access_token= would skip the
            // jti, sid and user-revocation checks alike. The comment this replaces cited WebSocket
            // support; there is no WebSocket, SignalR or hub anywhere in this solution, and the
            // uploads that are fetched by URL are served by UseStaticFiles with no authentication.
            OnAuthenticationFailed = context =>
            {
                if (context.Exception is SecurityTokenExpiredException)
                {
                    // Set, not appended: at userinfo both schemes read the same expired token.
                    context.Response.Headers["Token-Expired"] = "true";
                }
                return Task.CompletedTask;
            },
            // An expired token is answered with Http.TokenExpired (ADR 0001).
            OnChallenge = JwtChallengeReasons.Record
        };
    }
}
