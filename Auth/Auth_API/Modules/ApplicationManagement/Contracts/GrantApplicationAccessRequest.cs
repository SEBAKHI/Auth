namespace Auth_API.Modules.ApplicationManagement.Contracts;

/// <summary>
/// Invites a user to a restricted application.
/// </summary>
/// <param name="RoleId">
/// Optional role to assign, scoped to this application. The invitation only
/// opens the door, so without a role the invitee signs in able to do nothing;
/// supplying one here saves a second trip through the user's own page.
/// </param>
/// <param name="ExpiresAt">Optional expiry; the invitation lapses on its own.</param>
/// <param name="Note">Optional free-text reason, e.g. which trial this is for.</param>
public record GrantApplicationAccessRequest(
    Guid UserId,
    Guid? RoleId = null,
    DateTime? ExpiresAt = null,
    string? Note = null);
