namespace Auth_API.Modules.OrganizationManagement.Contracts;

/// <summary>
/// Request to invite a user to an organization.
/// </summary>
/// <param name="Email">Invitee email address.</param>
/// <param name="RoleId">Organization-level membership role.</param>
/// <param name="LanguageCode">
/// Optional language for the invitation email, chosen by the inviter. When null,
/// the invitee's profile language (for existing accounts) or the inviter's
/// request culture decides.
/// </param>
public record InviteMemberRequest(string Email, Guid RoleId, string? LanguageCode = null);
