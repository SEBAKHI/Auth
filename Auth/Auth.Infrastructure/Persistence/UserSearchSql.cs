namespace Auth.Infrastructure.Persistence;

/// <summary>
/// The one definition of "does this user match what was typed in a search box".
/// </summary>
/// <remarks>
/// Every user-facing list — the users page, an application's users, an
/// organization's members, a role's or permission's holders, and the trial-user
/// picker — must agree on this, because a person searching by name does not
/// know or care which screen they are on.
/// <para>
/// It was hand-written in seven places before this existed, and all seven
/// matched only Email, FirstName and LastName. Typing a person's full name
/// found nobody: "Le Ga" is FirstName "Le" and LastName "Ga", so it matched
/// neither column on its own, while <c>FullName</c> — a persisted computed
/// column holding exactly that string — was never consulted. Neither was
/// <c>Username</c>. Fixing one copy would have left the other six lying.
/// </para>
/// </remarks>
internal static class UserSearchSql
{
    /// <summary>
    /// Emits the search predicate for a <c>Users</c> row, to be AND-ed into a
    /// WHERE clause. Pass the table alias used by the enclosing query, or an
    /// empty string when the query selects from <c>Users</c> unaliased.
    /// </summary>
    /// <param name="alias">
    /// Table alias without the trailing dot, e.g. <c>"u"</c>. Repository-owned
    /// literal, never client input — no user-supplied text reaches the emitted
    /// SQL, which is why interpolating it here is safe.
    /// </param>
    /// <param name="parameter">
    /// Name of the LIKE parameter, defaulting to <c>@SearchPattern</c>. The
    /// caller binds it to <c>%term%</c>, or to null to disable filtering.
    /// </param>
    public static string Matches(string alias = "", string parameter = "@SearchPattern")
    {
        var prefix = string.IsNullOrEmpty(alias) ? string.Empty : $"{alias}.";

        // FullName is PERSISTED, so this is an index-friendly column read rather
        // than a per-row concatenation.
        return $@"({parameter} IS NULL OR
                   {prefix}[Email] LIKE {parameter} OR
                   {prefix}[Username] LIKE {parameter} OR
                   {prefix}[FullName] LIKE {parameter} OR
                   {prefix}[FirstName] LIKE {parameter} OR
                   {prefix}[LastName] LIKE {parameter})";
    }
}
