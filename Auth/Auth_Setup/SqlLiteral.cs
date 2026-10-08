namespace Auth_Setup;

/// <summary>
/// The one way Auth_Setup writes a value into the SQL it prints.
/// </summary>
internal static class SqlLiteral
{
    /// <summary>
    /// A Unicode string literal, <c>N'…'</c>, with every <c>'</c> doubled — so no
    /// input can end the literal and add a statement of its own. The address rules
    /// accept <c>'</c> and <c>;</c>, so this is not hypothetical.
    /// </summary>
    public static string Unicode(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
