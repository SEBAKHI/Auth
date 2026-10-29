using System.Net;
using System.Net.Sockets;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// The addresses an API key may be presented from (OI-128 PR B): single IPv4 or IPv6 addresses
/// and CIDR ranges, normalized. Empty means any address.
/// </summary>
/// <remarks>
/// The identity system stores and reports the list; the application's server, which sees the
/// caller's address, applies it (the SDK does). An IPv4 address written as IPv4-mapped IPv6 is
/// stored as IPv4, the form the SDK compares against once it has unmapped the caller's address.
/// </remarks>
public sealed class IpAllowList : IEquatable<IpAllowList>
{
    /// <summary>The most entries one key may carry.</summary>
    public const int MaxEntries = 50;

    /// <summary>The longest entry accepted, before parsing: an IPv6 range is at most 43 characters.</summary>
    private const int MaxEntryLength = 64;

    public static readonly IpAllowList Any = new([]);

    private IpAllowList(IReadOnlyList<string> entries)
    {
        Entries = entries;
    }

    /// <summary>The normalized entries, in the order given, without duplicates.</summary>
    public IReadOnlyList<string> Entries { get; }

    /// <summary>
    /// Validates and normalizes <paramref name="entries"/>: each an address or a CIDR range whose
    /// host bits are zero. Null or empty is <see cref="Any"/>.
    /// </summary>
    public static ErrorOr<IpAllowList> Create(IEnumerable<string>? entries)
    {
        var list = entries?.ToList() ?? [];
        if (list.Count > MaxEntries)
        {
            return ApiKeyErrors.AllowedIpsTooMany;
        }

        var normalized = new List<string>(list.Count);
        foreach (var entry in list)
        {
            var value = Normalize(entry);
            if (value is null)
            {
                return ApiKeyErrors.AllowedIpInvalid;
            }

            if (!normalized.Contains(value, StringComparer.Ordinal))
            {
                normalized.Add(value);
            }
        }

        return normalized.Count == 0 ? Any : new IpAllowList(normalized);
    }

    /// <summary>
    /// The list as stored: entries this type wrote, read back without validation. An entry that no
    /// longer parses is kept as written, so that it matches nothing rather than vanishing and
    /// widening the list.
    /// </summary>
    public static IpAllowList FromStored(IEnumerable<string>? entries)
    {
        var list = entries?.Where(entry => !string.IsNullOrWhiteSpace(entry)).ToList() ?? [];
        return list.Count == 0 ? Any : new IpAllowList(list);
    }

    /// <summary>Whether <paramref name="entry"/> is one address or range <see cref="Create"/> accepts.</summary>
    public static bool IsValidEntry(string? entry) => Normalize(entry) is not null;

    private static string? Normalize(string? entry)
    {
        var text = entry?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxEntryLength)
        {
            return null;
        }

        if (!text.Contains('/'))
        {
            return IPAddress.TryParse(text, out var address) && IsPlainAddress(text, address)
                ? Unmapped(address).ToString()
                : null;
        }

        // A range whose host bits are set ("10.0.0.1/24") is refused: a mask written around a host
        // address is more likely a mistake than a range. IPNetwork.TryParse masks such a value
        // silently, so the written address must be the range's own base address.
        var slash = text.IndexOf('/');
        if (!IPNetwork.TryParse(text, out var network)
            || !IPAddress.TryParse(text[..slash], out var written)
            || !IsPlainAddress(text[..slash], written)
            || !written.Equals(network.BaseAddress))
        {
            return null;
        }

        if (network.BaseAddress.IsIPv4MappedToIPv6 && network.PrefixLength >= 96)
        {
            return new IPNetwork(network.BaseAddress.MapToIPv4(), network.PrefixLength - 96).ToString();
        }

        return network.ToString();
    }

    /// <summary>
    /// IPAddress.TryParse also accepts forms no administrator means as an address: a bare number
    /// ("10" is 0.0.0.10), short dotted forms ("10.1"), and an IPv6 zone. Only the dotted quad and
    /// IPv6 text are taken.
    /// </summary>
    private static bool IsPlainAddress(string text, IPAddress address) =>
        address.AddressFamily switch
        {
            AddressFamily.InterNetwork => text.Count(ch => ch == '.') == 3,
            AddressFamily.InterNetworkV6 => !text.Contains('%'),
            _ => false
        };

    private static IPAddress Unmapped(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    public bool Equals(IpAllowList? other) =>
        other is not null && Entries.SequenceEqual(other.Entries, StringComparer.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as IpAllowList);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var entry in Entries)
        {
            hash.Add(entry, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
