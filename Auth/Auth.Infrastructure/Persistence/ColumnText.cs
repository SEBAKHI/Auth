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
    /// <summary>The value, cut to <paramref name="maxLength"/> characters when longer; null stays null.</summary>
    public static string? Truncate(string? value, int maxLength) =>
        value is not null && value.Length > maxLength ? value[..maxLength] : value;
}
