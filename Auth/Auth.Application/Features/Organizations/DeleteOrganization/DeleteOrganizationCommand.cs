using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.DeleteOrganization;

/// <summary>
/// Command to delete an organization.
/// Only the owner can delete an organization.
/// </summary>
public record DeleteOrganizationCommand(Guid OrganizationId) : IRequest<ErrorOr<Deleted>>
{
    /// <summary>
    /// The ID of the user requesting the deletion.
    /// </summary>
    public Guid RequestedBy { get; init; }
}
