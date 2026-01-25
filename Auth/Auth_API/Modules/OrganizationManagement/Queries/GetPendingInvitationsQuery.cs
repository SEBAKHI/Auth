using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.OrganizationManagement.Queries;

/// <summary>
/// Query to get pending invitations for an organization.
/// </summary>
public record GetPendingInvitationsQuery(Guid OrganizationId) : IRequest<ErrorOr<IReadOnlyList<OrganizationInvitationDto>>>
{
    /// <summary>
    /// The ID of the user making the request.
    /// </summary>
    public Guid RequestedBy { get; set; }
}
