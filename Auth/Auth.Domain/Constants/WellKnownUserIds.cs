namespace Auth.Domain.Constants;

/// <summary>
/// Identifiers of the accounts seeded by the platform itself.
/// </summary>
public static class WellKnownUserIds
{
    /// <summary>
    /// The internal system account that seed scripts and background jobs act as.
    /// It is the reattribution target when a purged user's actor references
    /// must be preserved, so it can never be removed itself.
    /// </summary>
    public static readonly Guid System = Guid.Parse("00000000-0000-0000-0000-000000000001");
}
