using System.Net;
using System.Net.Sockets;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using MaxMind.GeoIP2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Services;

/// <summary>
/// Resolves an approximate place name from a local MaxMind GeoLite2 database.
///
/// Registered as a singleton because <see cref="DatabaseReader"/> memory-maps the
/// file once and is thread-safe for reads; opening it per request would put file
/// I/O on the sign-in path for no benefit.
///
/// Fails open, everywhere. A missing file, a corrupt file, an address the
/// database does not know — all yield null, which the UI renders as no location
/// rather than as an error. Session creation already swallows its own failures
/// so a bookkeeping problem cannot cost someone their login; a location lookup
/// deserves no more authority than that.
/// </summary>
public sealed class GeoIpLookup : IGeoIpLookup, IDisposable
{
    private readonly DatabaseReader? _reader;
    private readonly ILogger<GeoIpLookup> _logger;

    public GeoIpLookup(
        IOptions<GeoIpSettings> settings,
        ILogger<GeoIpLookup> logger)
    {
        _logger = logger;

        var value = settings.Value;
        if (!value.Enabled || string.IsNullOrWhiteSpace(value.DatabasePath))
        {
            return;
        }

        try
        {
            _reader = new DatabaseReader(value.DatabasePath);
            _logger.LogInformation("GeoIP database loaded from {Path}", value.DatabasePath);
        }
        catch (Exception ex)
        {
            // Logged at warning, not error, and not rethrown: the alternative is
            // an application that will not start because it cannot name cities.
            _logger.LogWarning(ex,
                "GeoIP is enabled but the database at {Path} could not be opened; " +
                "locations will be left empty", value.DatabasePath);
        }
    }

    /// <inheritdoc />
    public string? Resolve(string? ipAddress)
    {
        if (_reader is null || !IPAddress.TryParse(ipAddress, out var parsed))
        {
            return null;
        }

        // Private, loopback and link-local addresses have no public location, and
        // asking about them is the common case in development.
        if (IsNotRoutable(parsed))
        {
            return null;
        }

        try
        {
            if (!_reader.TryCity(parsed, out var response) || response is null)
            {
                return null;
            }

            var city = response.City?.Name;
            var country = response.Country?.Name;

            return (city, country) switch
            {
                (null, null) => null,
                (not null, null) => city,
                (null, not null) => country,
                _ => $"{city}, {country}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GeoIP lookup failed");
            return null;
        }
    }

    private static bool IsNotRoutable(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
        }

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            10 => true,
            127 => true,
            169 when octets[1] == 254 => true,      // link-local
            172 when octets[1] >= 16 && octets[1] <= 31 => true,
            192 when octets[1] == 168 => true,
            _ => false
        };
    }

    public void Dispose() => _reader?.Dispose();
}
