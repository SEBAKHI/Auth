using Auth.Domain.Entities;
using Auth.Domain.ReadModels.Access;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// The single authority on "may this user sign in to this application?".
/// <para>
/// Every sign-in path — the authorize endpoint, the token exchange, and the
/// refresh — asks <see cref="IsUserEntitledAsync"/>, and nothing else answers
/// that question anywhere in the system. Keeping one definition is deliberate:
/// two implementations of an access rule eventually disagree, and the one that
/// is wrong is the one nobody is looking at.
/// </para>
/// </summary>
public interface IApplicationAccessRepository
{
    /// <summary>
    /// Decides whether the user may sign in to the application right now.
    /// True when the application exists, is not soft-deleted, is active, and
    /// either is open to everyone or holds a valid invitation for this user.
    /// </summary>
    /// <remarks>
    /// There is deliberately no bypass for platform administrators. The
    /// <c>applications:*</c> permissions govern administering an application's
    /// registration, not being one of its users; silently admitting super-admins
    /// to every partner application is exactly the surprise this gate exists to
    /// remove. An administrator who needs in grants themselves an invitation,
    /// which is one click and leaves an audit record.
    /// </remarks>
    Task<bool> IsUserEntitledAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the applications the user may sign in to — the same rule as
    /// <see cref="IsUserEntitledAsync"/>, asked the other way round.
    /// </summary>
    Task<IReadOnlyList<UserApplicationAccess>> GetApplicationsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the standing invitations on an application's access list (active,
    /// unrevoked, unexpired), with the invited users' display details.
    /// </summary>
    Task<IReadOnlyList<ApplicationUserGrantRow>> GetGrantsAsync(
        Guid applicationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the invitation row for a user, in any state, or null when the user
    /// was never invited. Callers use this to tell "never invited" from
    /// "invited then revoked", which must reactivate the existing row rather
    /// than insert a second one.
    /// </summary>
    Task<ApplicationUserAccess?> GetGrantAsync(
        Guid applicationId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a new invitation.
    /// </summary>
    Task CreateGrantAsync(
        ApplicationUserAccess grant,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists a change to an existing invitation — a revocation or a
    /// reinstatement of the same row.
    /// </summary>
    Task UpdateGrantAsync(
        ApplicationUserAccess grant,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether the user currently holds a valid invitation, without
    /// considering the application's access mode.
    /// </summary>
    Task<bool> HasActiveGrantAsync(
        Guid applicationId,
        Guid userId,
        CancellationToken cancellationToken);
}
