using Auth.Domain.Enums;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Builds ORDER BY clauses from a repository-owned allow-list map. Client input
/// is only ever used as a dictionary key — the emitted SQL comes exclusively
/// from the hard-coded column expressions, so injection is impossible.
/// </summary>
internal static class SortSql
{
    /// <summary>
    /// Creates the case-insensitive sort-field → column-expressions map.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> Map(
        params (string Field, string[] Columns)[] entries)
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (field, columns) in entries)
        {
            map[field] = columns;
        }
        return map;
    }

    /// <summary>
    /// Returns the ORDER BY body for the requested sort, or
    /// <paramref name="defaultOrderBy"/> when <paramref name="sortBy"/> is null
    /// or unmapped. <paramref name="tieBreaker"/> (typically the primary key) is
    /// appended for stable OFFSET/FETCH pagination.
    /// </summary>
    public static string OrderBy(
        IReadOnlyDictionary<string, string[]> columnMap,
        string? sortBy,
        SortDirection sortDirection,
        string defaultOrderBy,
        string tieBreaker)
    {
        if (string.IsNullOrEmpty(sortBy) || !columnMap.TryGetValue(sortBy, out var columns))
        {
            return $"{defaultOrderBy}, {tieBreaker}";
        }

        var direction = sortDirection == SortDirection.Desc ? "DESC" : "ASC";
        var parts = columns.Select(column => $"{column} {direction}").Append(tieBreaker);
        return string.Join(", ", parts);
    }
}
