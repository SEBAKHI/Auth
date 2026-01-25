using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.OrganizationManagement.Commands;

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
