using Auth.Application.Configuration;

namespace Auth.Application.Common;

/// <summary>
/// Whether an absolute address belongs to one of the platform's own browser apps (the console,
/// the accounts app), as listed in <see cref="IdentityProviderSettings.FirstPartySpaOrigins"/>.
/// </summary>
/// <remarks>
/// <para>
/// Compares origins as a browser does: scheme, host and port, letter case and a default port
/// ignored, so <c>https://Accounts.example.com:443/</c> and <c>https://accounts.example.com/x</c>
/// are the same app and <c>https://accounts.example.com:8443</c> is another.
/// </para>
/// <para>
/// Not the API's <c>FirstPartyOriginResolver</c>, which answers a different question from a
/// different input: whether a request's <c>Origin</c> header (always a bare origin) may spend a
/// refresh cookie. This one reads a full address, path and all, taken from an application's
/// registration.
/// </para>
/// <para>
/// An empty list (the default) matches nothing: a platform app registered as an application is
/// then treated like any other.
/// </para>
/// </remarks>
public sealed class FirstPartyAddresses
{
    private readonly HashSet<string> _origins;

    public FirstPartyAddresses(IEnumerable<string> configuredOrigins)
    {
        _origins = configuredOrigins
            .Select(OriginOf)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The list as configured now (read per request through the options snapshot).</summary>
    public static FirstPartyAddresses From(IdentityProviderSettings settings) => new(settings.FirstPartySpaOrigins);

    /// <summary>Whether <paramref name="address"/> opens one of the platform's own browser apps.</summary>
    public bool Contains(string address) => OriginOf(address) is { } origin && _origins.Contains(origin);

    private static string? OriginOf(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant()
            : null;
}
