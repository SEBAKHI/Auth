namespace Auth.Domain.Enums;

/// <summary>
/// Decides who may sign in to an application that is switched on.
/// <para>
/// This is the second of two independent switches, and it is the weaker one:
/// <c>Application.IsActive</c> answers "is the application switched on at all?"
/// and beats everything, so a deactivated application admits nobody regardless
/// of the mode. The access mode is only consulted for an application that is
/// already active.
/// </para>
/// </summary>
public enum ApplicationAccessMode
{
    /// <summary>
    /// Any authenticated, non-locked-out platform user may sign in. The
    /// invitation list (<c>ApplicationUserAccess</c>) is not read at all.
    /// </summary>
    Everyone = 1,

    /// <summary>
    /// Only users holding an active, unrevoked, unexpired row in
    /// <c>ApplicationUserAccess</c> may sign in — nothing else admits anyone,
    /// not an organization membership, not an application-scoped role, and not
    /// platform administration permissions. This is the default for newly
    /// created applications, and it implies the application has no enabled
    /// organizations (see <c>ApplicationErrors.RestrictedCannotBeEnabledForOrganization</c>).
    /// </summary>
    Restricted = 2
}
