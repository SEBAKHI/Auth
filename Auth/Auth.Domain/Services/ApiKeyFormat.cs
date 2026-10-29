namespace Auth.Domain.Services;

/// <summary>
/// The shape of an API key (OI-128 PR B): <c>ak_&lt;env&gt;_&lt;selector&gt;_&lt;secret&gt;&lt;check&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The selector is random and stored in clear, so validation finds the one row it names and runs
/// one Argon2id verify of the whole key against that row's hash: an unknown selector costs a
/// lookup, never a hash. The check is CRC32 over everything before it, so a mistyped or random
/// string is refused before any lookup, here and in the SDK, and a secret scanner can tell a real
/// key from look-alike text. It stops no deliberate forger: the algorithm is public.
/// </para>
/// <para>
/// Rollback rule: the stored prefix is the text up to the second underscore, and the hash covers
/// the whole key, so the validation that came before this format (prefix up to the second
/// underscore, then verify) still accepts every key this format mints. Hence every key carries
/// exactly one environment segment, <see cref="OtherEnvironmentSegment"/> included.
/// </para>
/// </remarks>
public static class ApiKeyFormat
{
    /// <summary>The characters of the selector, the secret and the check: 0-9, A-Z, a-z.</summary>
    public const string Base62Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public const int SelectorLength = 16;
    public const int SecretLength = 32;
    public const int ChecksumLength = 6;

    /// <summary>The segment of every environment other than the three named ones.</summary>
    public const string OtherEnvironmentSegment = "othr";

    private const string Scheme = "ak";

    private static readonly string[] Segments = ["prod", "stag", "dev", OtherEnvironmentSegment];

    /// <summary>The environment segment a key for <paramref name="environment"/> carries.</summary>
    public static string EnvironmentSegment(string environment) => environment.Trim().ToLowerInvariant() switch
    {
        "production" => "prod",
        "staging" => "stag",
        "development" => "dev",
        _ => OtherEnvironmentSegment
    };

    /// <summary>The stored prefix of a key for <paramref name="environment"/>: up to the second underscore.</summary>
    public static string PrefixFor(string environment) => $"{Scheme}_{EnvironmentSegment(environment)}_";

    /// <summary>
    /// The plain key for <paramref name="environment"/>, from a selector and a secret that are
    /// already random base62 of the right lengths.
    /// </summary>
    /// <exception cref="ArgumentException">A part is not base62 of its length: a programming error.</exception>
    public static string Compose(string environment, string selector, string secret)
    {
        if (!IsBase62(selector, SelectorLength))
        {
            throw new ArgumentException($"The selector must be {SelectorLength} base62 characters.", nameof(selector));
        }

        if (!IsBase62(secret, SecretLength))
        {
            throw new ArgumentException($"The secret must be {SecretLength} base62 characters.", nameof(secret));
        }

        var body = $"{PrefixFor(environment)}{selector}_{secret}";
        return body + Checksum(body);
    }

    /// <summary>
    /// Reads the stored prefix and the selector of <paramref name="rawKey"/>, when it has this
    /// format exactly and its check matches; false for anything else, a key minted before this
    /// format included.
    /// </summary>
    public static bool TryParse(string? rawKey, out string prefix, out string selector)
    {
        prefix = string.Empty;
        selector = string.Empty;

        if (rawKey is null)
        {
            return false;
        }

        var parts = rawKey.Split('_');
        if (parts.Length != 4
            || !string.Equals(parts[0], Scheme, StringComparison.Ordinal)
            || !Segments.Contains(parts[1], StringComparer.Ordinal)
            || !IsBase62(parts[2], SelectorLength)
            || !IsBase62(parts[3], SecretLength + ChecksumLength))
        {
            return false;
        }

        var body = rawKey[..^ChecksumLength];
        if (!string.Equals(rawKey[^ChecksumLength..], Checksum(body), StringComparison.Ordinal))
        {
            return false;
        }

        prefix = $"{Scheme}_{parts[1]}_";
        selector = parts[2];
        return true;
    }

    /// <summary>CRC32 (IEEE 802.3) of the UTF-8 bytes, as six base62 digits, most significant first.</summary>
    private static string Checksum(string body)
    {
        var value = Crc32(body);
        Span<char> digits = stackalloc char[ChecksumLength];
        for (var i = ChecksumLength - 1; i >= 0; i--)
        {
            digits[i] = Base62Alphabet[(int)(value % 62)];
            value /= 62;
        }

        return new string(digits);
    }

    private static uint Crc32(string body)
    {
        // Base62 and underscores only by the time this runs, so the UTF-8 bytes are the chars.
        var crc = 0xFFFFFFFFu;
        foreach (var ch in body)
        {
            crc ^= (byte)ch;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static bool IsBase62(string value, int length)
    {
        if (value.Length != length)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!char.IsAsciiLetterOrDigit(ch))
            {
                return false;
            }
        }

        return true;
    }
}
