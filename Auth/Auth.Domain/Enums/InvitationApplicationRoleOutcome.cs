namespace Auth.Domain.Enums;

/// <summary>
/// What accepting an invitation did with the application role it named. Recorded on the
/// invitation, in the acceptance's own transaction, so that an invitee who joined without
/// the role can be told why, and an organization can see it, instead of a log line nobody
/// reads being the only trace.
/// <para>
/// Stored by name (<c>OrganizationInvitations.ApplicationRoleOutcome</c>), so the members
/// may be reordered but never renamed. An invitation that names no application role has no
/// outcome at all, never one of these.
/// </para>
/// </summary>
public enum InvitationApplicationRoleOutcome
{
    /// <summary>A new assignment of the role was written for the invitee.</summary>
    Applied = 1,

    /// <summary>The invitee already held that role in that organization; nothing was written.</summary>
    AlreadyHeld = 2,

    /// <summary>
    /// The inviter could no longer give the role when it was accepted (the organization
    /// disabled the application, or the inviter lost the authority), so the invitee joined
    /// with the membership role only.
    /// </summary>
    Dropped = 3
}
