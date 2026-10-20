using System.Threading.RateLimiting;
using Auth.Domain.Constants;
using Auth.Shared.Http;
using Auth_API.Common.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace Auth_API.Common.Middleware;

/// <summary>
/// Decides, before the rate limiter, which budget an application-directory request counts in
/// (OI-194): its application's, when it carries an application access token that validates; its
/// address's otherwise. Runs only on endpoints that opt into the <see cref="PolicyName"/> policy.
/// </summary>
/// <remarks>
/// <para>
/// <c>UseAuthentication</c> runs the platform scheme alone, so an application's token reaches the
/// limiter as an anonymous principal and the limiter cannot tell one application from another.
/// This step authenticates the request with the scheme the endpoint itself names in its
/// <c>[Authorize]</c>, and only on those endpoints: no other path is authenticated differently.
/// The handler keeps its answer for the request, so the token is validated once.
/// </para>
/// <para>
/// Only a token whose signature, issuer, lifetime and single application audience validate
/// reaches an application's budget. Counting on the <c>aud</c> a request merely claims would let
/// anyone empty another application's budget with unsigned tokens that name it.
/// </para>
/// </remarks>
public sealed class ApplicationReadClassificationMiddleware
{
    /// <summary>The rate-limit policy this step classifies for; Program.cs registers it under this name.</summary>
    public const string PolicyName = "app-read";

    private const string ItemKey = "Auth_API.ApplicationReadCaller";

    private readonly RequestDelegate _next;

    public ApplicationReadClassificationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == PolicyName)
        {
            var application = await ValidatedApplicationAsync(context, endpoint);
            if (application is not null)
            {
                context.Items[ItemKey] = application;
            }
        }

        await _next(context);
    }

    /// <summary>
    /// The partition this request counts in: its validated application's, or its address's.
    /// Keys carry the settings version, like every other policy's, so a saved limit applies to
    /// fresh partitions.
    /// </summary>
    public static RateLimitPartition<string> Partition(
        HttpContext context,
        int settingsVersion,
        Func<FixedWindowBudget> perApplication,
        Func<FixedWindowBudget> perAddress) =>
        context.Items.TryGetValue(ItemKey, out var marked) && marked is string application
            // "|", never ":": an address key is "v{version}:" followed by whatever address the
            // resolver read, so no address can spell an application's key.
            ? RateLimitPartition.GetFixedWindowLimiter($"v{settingsVersion}|app|{application}", _ => perApplication().Options())
            : RateLimitPartition.GetFixedWindowLimiter(
                $"v{settingsVersion}:{ClientIpResolver.Resolve(context) ?? "unknown"}", _ => perAddress().Options());

    /// <summary>
    /// The audience of the request's token when the endpoint's own scheme validates it, or null.
    /// The endpoint names exactly one scheme; anything else is left to its authorization, which
    /// refuses it after the limiter as before.
    /// </summary>
    private static async Task<string?> ValidatedApplicationAsync(HttpContext context, Endpoint endpoint)
    {
        var schemes = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(data => data.AuthenticationSchemes)
            .Where(scheme => !string.IsNullOrWhiteSpace(scheme))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (schemes.Count != 1 || schemes[0]!.Contains(',', StringComparison.Ordinal))
        {
            return null;
        }

        var result = await context.AuthenticateAsync(schemes[0]);
        return result.Succeeded
            && result.Principal.FindFirst(JwtClaimNames.Audience)?.Value is { Length: > 0 } application
            ? application
            : null;
    }
}
