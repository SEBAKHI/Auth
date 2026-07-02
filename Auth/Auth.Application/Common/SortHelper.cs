using Auth.Domain.Enums;

namespace Auth.Application.Common;

/// <summary>
/// In-memory sorting for handler-assembled DTO lists (lists enriched after the
/// SQL query, where the sortable fields don't exist as columns). SQL-backed
/// lists sort in their repository instead; see the per-repository allow-lists.
/// </summary>
public static class SortHelper
{
    /// <summary>
    /// Orders <paramref name="items"/> by the selector registered for
    /// <paramref name="sortBy"/> (case-insensitive). Returns the list unchanged
    /// when <paramref name="sortBy"/> is null or unknown, preserving the
    /// endpoint's default order. String values compare case-insensitively for
    /// consistency with SQL collation ordering.
    /// </summary>
    public static IReadOnlyList<TDto> Apply<TDto>(
        IEnumerable<TDto> items,
        string? sortBy,
        SortDirection sortDirection,
        IReadOnlyDictionary<string, Func<TDto, object?>> selectors)
    {
        var list = items as IReadOnlyList<TDto> ?? items.ToList();
        if (sortBy is null || !selectors.TryGetValue(sortBy, out var selector))
        {
            return list;
        }

        var comparer = new NormalizingComparer();
        return sortDirection == SortDirection.Desc
            ? list.OrderByDescending(selector, comparer).ToList()
            : list.OrderBy(selector, comparer).ToList();
    }

    /// <summary>
    /// Builds the case-insensitive selector map used with
    /// <see cref="Apply{TDto}"/> so feature code stays declarative.
    /// </summary>
    public static IReadOnlyDictionary<string, Func<TDto, object?>> Selectors<TDto>(
        params (string Field, Func<TDto, object?> Selector)[] entries)
    {
        var map = new Dictionary<string, Func<TDto, object?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (field, selector) in entries)
        {
            map[field] = selector;
        }
        return map;
    }

    /// <summary>
    /// Compares selector results: strings case-insensitively, other values via
    /// their natural comparison; nulls sort first ascending.
    /// </summary>
    private sealed class NormalizingComparer : IComparer<object?>
    {
        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            if (x is string left && y is string right)
            {
                return StringComparer.OrdinalIgnoreCase.Compare(left, right);
            }
            return Comparer<object>.Default.Compare(x, y);
        }
    }
}
