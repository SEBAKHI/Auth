namespace Auth.Domain.Enums;

/// <summary>
/// Direction applied to a client-requested sort field on list queries.
/// </summary>
public enum SortDirection
{
    /// <summary>
    /// Ascending order (A→Z, oldest→newest).
    /// </summary>
    Asc = 0,

    /// <summary>
    /// Descending order (Z→A, newest→oldest).
    /// </summary>
    Desc = 1
}
