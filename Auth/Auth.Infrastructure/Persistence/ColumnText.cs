namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Fits client-supplied text (a user agent, a device id) to the column that stores it.
/// </summary>
/// <remarks>
/// The client chooses these values, so an over-long one is cut to the column's width here
/// rather than allowed to fail the INSERT, and with it the sign-in or the authorize request
/// that writes it. Each repository names its own column widths.
/// </remarks>
internal static class ColumnText
{
    /// <summary>
    /// The value, cut to at most <paramref name="maxLength"/> characters when longer, never
    /// between the two halves of a surrogate pair. Null stays null and empty stays empty: on a
    /// code, NULL means a row written before the column (OI-178).
    /// </summary>
    public static string? Truncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        // A lone high surrogate would be stored, and shown as a replacement character.
        var cut = char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
        return value[..cut];
    }
}
