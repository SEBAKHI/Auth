namespace Auth.Domain.Constants;

/// <summary>
/// What a recovery code looks like: eight characters from an alphabet without the
/// look-alike ones, shown as <c>XXXX-XXXX</c>. Dashes and spaces are not part of the
/// code and case does not matter, so the issue, the comparison and the shape check
/// all read the same normalized text.
/// </summary>
public static class RecoveryCodeFormat
{
    /// <summary>The characters a recovery code is drawn from: no 0/O, no 1/I.</summary>
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Characters in a recovery code, without its dash.</summary>
    public const int Length = 8;

    /// <summary>The code without dashes or spaces, upper-cased.</summary>
    public static string Normalize(string code) =>
        code.Replace("-", "").Replace(" ", "").ToUpperInvariant();

    /// <summary>
    /// Whether the input could be a recovery code at all. A field that carries any of
    /// the account's codes routes by it: what has this shape is checked as a recovery
    /// code, anything else as the factor's own code.
    /// </summary>
    public static bool HasShape(string code)
    {
        var normalized = Normalize(code);
        return normalized.Length == Length && normalized.All(Alphabet.Contains);
    }
}
