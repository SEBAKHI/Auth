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
    public Guid InvitedBy { get; set; }
}
