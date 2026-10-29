namespace Auth.Sdk.Validation;

/// <summary>
/// The offline shape check of an API key (SDK 2.0): <c>ak_&lt;env&gt;_&lt;selector&gt;_&lt;secret&gt;&lt;check&gt;</c>,
/// with a CRC32 check over everything before it. A mistyped or random value is refused here,
/// before any network call; a well-formed forgery still costs one validation call.
/// </summary>
/// <remarks>
/// The AuthSystem's own copy of this rule is <c>Auth.Domain.Services.ApiKeyFormat</c>; the SDK
/// ships alone, so it carries its own, and a test holds the two together.
/// </remarks>
internal static class ApiKeyShape
{
    private const int SelectorLength = 16;
    private const int SecretAndCheckLength = 38;
    private const int CheckLength = 6;
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private static readonly string[] Segments = ["prod", "stag", "dev", "othr"];

    public static bool IsWellFormed(string? rawKey)
    {
        if (rawKey is null)
        {
            return false;
        }

        var parts = rawKey.Split('_');
        if (parts.Length != 4
            || parts[0] != "ak"
            || !Segments.Contains(parts[1], StringComparer.Ordinal)
            || !IsBase62(parts[2], SelectorLength)
            || !IsBase62(parts[3], SecretAndCheckLength))
        {
            return false;
        }

        return string.Equals(rawKey[^CheckLength..], Check(rawKey[..^CheckLength]), StringComparison.Ordinal);
    }

    private static string Check(string body)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var ch in body)
        {
            crc ^= (byte)ch;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        var value = ~crc;
        Span<char> digits = stackalloc char[CheckLength];
        for (var i = CheckLength - 1; i >= 0; i--)
        {
            digits[i] = Alphabet[(int)(value % 62)];
            value /= 62;
        }

        return new string(digits);
    }

    private static bool IsBase62(string value, int length) =>
        value.Length == length && value.All(char.IsAsciiLetterOrDigit);
}
