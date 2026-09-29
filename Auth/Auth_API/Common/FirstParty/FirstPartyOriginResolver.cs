using System.Security.Cryptography;
using System.Text;
using Auth.Application.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Auth_API.Common.FirstParty;

/// <summary>
/// Tells whether a request comes from one of the platform's own browser apps.
/// </summary>
public interface IFirstPartyOriginResolver
{
    /// <summary>
    /// Gets whether any first-party origin is configured. With none, no request is
    /// first-party and nothing that depends on the list refuses anyone.
    /// </summary>
    bool HasOrigins { get; }

    /// <summary>
    /// The app whose origin the request's <c>Origin</c> header names exactly, or
    /// <c>null</c> when it names none (absent, <c>"null"</c>, http, unlisted).
    /// </summary>
    FirstPartyApp? Resolve(HttpRequest request);
}

/// <summary>
/// Matches the request's <c>Origin</c> header, exactly, against
/// <c>IdentityProvider:FirstPartySpaOrigins</c>.
/// </summary>
/// <remarks>
/// The Origin header is the one request fact a page script cannot choose: the
/// browser sets it on every cross-origin request and on every POST, and it is a
/// forbidden header name for fetch. That is why it, and not a cookie or a custom
/// header, decides which app a refresh cookie belongs to. Only the case and a
/// trailing slash are normalized; anything else — a sibling subdomain, the apex,
/// a path, http — is a different origin and is not first-party.
/// </remarks>
public sealed class FirstPartyOriginResolver : IFirstPartyOriginResolver
{
    private const string RefreshCookiePrefix = "__Host-auth_rt_";

    private readonly string[] _origins;

    /// <summary>
    /// Builds the resolver for the list as it is configured NOW. Registered per
    /// request from <c>IOptionsSnapshot</c>, so a list saved in the console applies
    /// to the next request.
    /// </summary>
    public static FirstPartyOriginResolver From(IOptionsSnapshot<IdentityProviderSettings> settings) =>
        new(settings.Value.FirstPartySpaOrigins);

    public FirstPartyOriginResolver(IEnumerable<string> configuredOrigins)
    {
        _origins = configuredOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public bool HasOrigins => _origins.Length > 0;

    /// <inheritdoc />
    public FirstPartyApp? Resolve(HttpRequest request) =>
        Resolve(request.Headers[HeaderNames.Origin].ToString());

    /// <summary>
    /// The app for a raw <c>Origin</c> header value; see <see cref="Resolve(HttpRequest)"/>.
    /// </summary>
    public FirstPartyApp? Resolve(string? originHeader)
    {
        if (string.IsNullOrWhiteSpace(originHeader))
        {
            return null;
        }

        var origin = Normalize(originHeader);

        // "null" is what an opaque origin (a sandboxed frame, a file, a redirect
        // chain that crossed origins) sends. It names no app, whatever is listed.
        // http can never carry a __Host- cookie, so it is never first-party either.
        if (!origin.StartsWith("https://", StringComparison.Ordinal) ||
            Array.IndexOf(_origins, origin) < 0)
        {
            return null;
        }

        return new FirstPartyApp(origin, CookieNameFor(origin));
    }

    /// <summary>
    /// The refresh cookie name for a normalized origin.
    /// </summary>
    public static string CookieNameFor(string normalizedOrigin)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedOrigin));
        return RefreshCookiePrefix + Convert.ToHexStringLower(digest)[..16];
    }

    private static string Normalize(string origin) =>
        origin.Trim().TrimEnd('/').ToLowerInvariant();
}
