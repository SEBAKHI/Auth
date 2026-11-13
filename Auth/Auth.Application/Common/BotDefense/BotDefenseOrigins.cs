using Auth.Application.Configuration;
using Microsoft.Extensions.Configuration;

namespace Auth.Application.Common.BotDefense;

/// <summary>
/// The browser origins whose pages may carry a bot-check token (D-S13-3 (a)): the platform's own
/// apps, <c>IdentityProvider:FirstPartySpaOrigins</c>; only while that list is empty, the origins
/// of <c>Cors:AllowedOrigins</c>. Cloudflare's answer names the host the token was made on, and a
/// token from any other host is refused. The verdict line's origin comes from the same list, so
/// the per-origin pass rate counts exactly the apps the check accepts.
/// </summary>
/// <remarks>
/// Read per request from the live configuration: both lists are hot settings. Blank entries are
/// the database layer's array tombstones and count for nothing.
/// </remarks>
public sealed class BotDefenseOrigins
{
    /// <summary>The verdict line's origin for a request whose Origin is not one of the listed apps.</summary>
    public const string Other = "other";

    private readonly HashSet<string> _origins;
    private readonly HashSet<string> _hostnames;

    public BotDefenseOrigins(IEnumerable<string?> firstPartyOrigins, IEnumerable<string?> corsOrigins)
    {
        var listed = Parse(firstPartyOrigins);
        if (listed.Count == 0)
        {
            listed = Parse(corsOrigins);
        }

        _origins = listed.Select(uri => uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        _hostnames = listed.Select(uri => uri.IdnHost.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The lists as configured now.</summary>
    public static BotDefenseOrigins From(IdentityProviderSettings identityProvider, IConfiguration configuration) =>
        new(
            identityProvider.FirstPartySpaOrigins,
            configuration.GetSection("Cors:AllowedOrigins").GetChildren().Select(child => child.Value));

    /// <summary>The hosts a token may have been made on.</summary>
    public IReadOnlyCollection<string> Hostnames => _hostnames;

    /// <summary>Whether Cloudflare's <c>hostname</c> is one of the listed apps' hosts.</summary>
    public bool AcceptsHostname(string? hostname) =>
        !string.IsNullOrWhiteSpace(hostname) && _hostnames.Contains(hostname.Trim().ToLowerInvariant());

    /// <summary>
    /// The request's <c>Origin</c> as the verdict line records it: the listed app it names, or
    /// <see cref="Other"/> for anything else, an absent header included.
    /// </summary>
    public string Label(string? requestOrigin) =>
        !string.IsNullOrWhiteSpace(requestOrigin)
        && Uri.TryCreate(requestOrigin.Trim(), UriKind.Absolute, out var uri)
        && _origins.Contains(uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant())
            ? uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant()
            : Other;

    private static List<Uri> Parse(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Uri.TryCreate(value!.Trim(), UriKind.Absolute, out var uri) ? uri : null)
            .Where(uri => uri is not null && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            .Cast<Uri>()
            .ToList();
}
