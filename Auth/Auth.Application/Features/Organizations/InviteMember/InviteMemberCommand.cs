using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.InviteMember;

/// <summary>
/// Command to invite a user to an organization.
/// </summary>
public record InviteMemberCommand(
    Guid OrganizationId,
    string Email,
    Guid RoleId) : IRequest<ErrorOr<OrganizationInvitationDto>>
{
    /// <summary>
    /// The ID of the user sending the invitation.
    /// </summary>
    public Guid InvitedBy { get; init; }

    /// <summary>
    /// Optional language for the invitation email, chosen by the inviter.
    /// When null, the invitee's profile language (for existing accounts) or the
    /// inviter's request culture decides.
    /// </summary>
    public string? LanguageCode { get; init; }

    /// <summary>
    /// Optional application role to assign on acceptance, alongside the membership
    /// role, so onboarding a member into an application takes one step.
    /// </summary>
    public InviteApplicationRole? ApplicationRole { get; init; }

    /// <summary>
    /// Whether the inviter may assign application roles in this organization
    /// (<c>org:permissions:manage</c>), resolved by the endpoint through the same
    /// authorization gate that guards the assignment endpoint. Inviting needs only
    /// <c>org:members:invite</c>; naming an application role also needs this.
    /// </summary>
    public bool CanAssignApplicationRoles { get; init; }

    /// <summary>
    /// Whether the inviter's token carries platform permissions, for the grant check
    /// on the application role.
    /// </summary>
    public bool PlatformAuthorityInToken { get; init; }
}

/// <summary>
/// The application role an invitation should assign on acceptance.
/// </summary>
public record InviteApplicationRole(Guid ApplicationId, Guid RoleId);
