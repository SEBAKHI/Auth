using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GrantApplicationAccess;

/// <summary>
/// Invites a user to an application: adds them to its access list so a
/// restricted application will let them sign in.
/// </summary>
/// <param name="RoleId">
/// Optional. The invitation opens the door and grants no authority, so a trial
/// user admitted with no role signs in able to do nothing. Supplying a role here
/// assigns it scoped to this application in the same step, sparing the
/// administrator a second trip through every invitee's page.
/// </param>
/// <param name="ExpiresAt">
/// Optional. A trial invitation given an expiry lapses without anyone having to
/// remember it.
/// </param>
public record GrantApplicationAccessCommand(
    Guid ApplicationId,
    Guid UserId,
    Guid? RoleId = null,
    DateTime? ExpiresAt = null,
    string? Note = null) : IRequest<ErrorOr<Success>>
{
    /// <summary>
    /// The ID of the administrator issuing the invitation (for audit).
    /// </summary>
    public Guid GrantedBy { get; init; }
}
