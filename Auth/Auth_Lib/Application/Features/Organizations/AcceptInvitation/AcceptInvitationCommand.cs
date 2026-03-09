using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Organizations.AcceptInvitation;

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
