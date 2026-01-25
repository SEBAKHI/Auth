using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.OrganizationManagement.Commands;

/// <summary>
/// Command to accept an organization invitation.
/// </summary>
public record AcceptInvitationCommand(string Token) : IRequest<ErrorOr<InvitationAcceptResultDto>>
{
    /// <summary>
    /// The ID of the user accepting the invitation.
    /// </summary>
    public Guid AcceptedBy { get; set; }
}
