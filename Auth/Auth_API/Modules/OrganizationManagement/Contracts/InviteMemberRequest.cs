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
/// <param name="ApplicationRole">
/// Optional application role to assign when the invitation is accepted, alongside the
/// membership role. Naming one also requires <c>org:permissions:manage</c>, and the
/// role must be one the inviter could assign directly.
/// </param>
public record InviteMemberRequest(
    string Email,
    Guid RoleId,
    string? LanguageCode = null,
    InviteApplicationRoleRequest? ApplicationRole = null);

/// <summary>
/// An application role named in an invitation.
/// </summary>
/// <param name="ApplicationId">An application the organization has enabled.</param>
/// <param name="RoleId">One of that application's roles.</param>
public record InviteApplicationRoleRequest(Guid ApplicationId, Guid RoleId);
