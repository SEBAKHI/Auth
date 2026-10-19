namespace Auth.Domain.ValueObjects;

/// <summary>
/// The application role an invitation hands its invitee on acceptance, alongside the
/// membership role: which application, and which of that application's roles.
/// </summary>
/// <remarks>
/// One value rather than two nullable ids on the invitation, so "an application with no
/// role" or "a role with no application" cannot be represented. The database repeats the
/// rule as a CHECK constraint on the two columns.
/// </remarks>
public sealed class InvitationApplicationRole : IEquatable<InvitationApplicationRole>
{
    /// <summary>Gets the application the role belongs to.</summary>
    public Guid ApplicationId { get; }

    /// <summary>Gets the application role to assign on acceptance.</summary>
    public Guid RoleId { get; }

    private InvitationApplicationRole(Guid applicationId, Guid roleId)
    {
        ApplicationId = applicationId;
        RoleId = roleId;
    }

    /// <summary>
    /// Creates the value. Both ids are required; the request validator rejects an empty
    /// one before it reaches here, so an empty id is a programming error.
    /// </summary>
    public static InvitationApplicationRole From(Guid applicationId, Guid roleId)
    {
        if (applicationId == Guid.Empty)
            throw new ArgumentException("An application id is required.", nameof(applicationId));
        if (roleId == Guid.Empty)
            throw new ArgumentException("A role id is required.", nameof(roleId));

        return new InvitationApplicationRole(applicationId, roleId);
    }

    /// <summary>
    /// Rebuilds the value from the two nullable columns: both set, or neither.
    /// </summary>
    public static InvitationApplicationRole? FromNullable(Guid? applicationId, Guid? roleId)
    {
        return applicationId is Guid application && roleId is Guid role
            ? From(application, role)
            : null;
    }

    public bool Equals(InvitationApplicationRole? other) =>
        other is not null && ApplicationId == other.ApplicationId && RoleId == other.RoleId;

    public override bool Equals(object? obj) => obj is InvitationApplicationRole other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ApplicationId, RoleId);
}
