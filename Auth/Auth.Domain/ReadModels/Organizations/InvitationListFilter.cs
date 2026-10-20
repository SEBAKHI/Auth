using Auth.Domain.Entities;

namespace Auth.Domain.ReadModels.Organizations;

/// <summary>
/// What an organization's invitation list is narrowed to.
/// </summary>
/// <param name="Statuses">
/// Effective statuses (<see cref="OrganizationInvitation.EffectiveStatus"/>) to return. Empty
/// keeps the list's historical content: every invitation STORED as pending, live or past its
/// expiry, which is what the list returned before it could be filtered, so a caller that sends
/// no status sees no change.
/// </param>
public sealed record InvitationListFilter(IReadOnlyCollection<InvitationStatus> Statuses)
{
    /// <summary>The unfiltered list: stored-pending invitations, live and expired.</summary>
    public static InvitationListFilter StoredPending { get; } = new([]);
}
