using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.ResendInvitation;

/// <summary>
/// Command to resend an organization invitation with a new token.
/// </summary>
public record ResendInvitationCommand(
    Guid OrganizationId,
    Guid InvitationId) : IRequest<ErrorOr<OrganizationInvitationDto>>
{
    /// <summary>
    /// The ID of the user resending the invitation.
    /// </summary>
    public Guid ResentBy { get; init; }
}
