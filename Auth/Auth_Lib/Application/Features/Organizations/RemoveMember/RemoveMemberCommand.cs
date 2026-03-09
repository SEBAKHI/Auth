using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Organizations.RemoveMember;

/// <summary>
/// Command to remove a member from an organization.
/// </summary>
public record RemoveMemberCommand(
    Guid OrganizationId,
    Guid UserId) : IRequest<ErrorOr<Deleted>>
{
    /// <summary>
    /// The ID of the user performing the removal.
    /// </summary>
    public Guid RemovedBy { get; set; }
}
