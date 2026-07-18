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
}
