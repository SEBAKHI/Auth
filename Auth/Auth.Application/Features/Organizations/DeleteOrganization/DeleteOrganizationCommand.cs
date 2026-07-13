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

    /// <summary>
    /// True when the caller holds the platform-wide organizations manage
    /// permission — allows deleting without being the owner. Set by the
    /// controller from JWT claims only, never bound from the request.
    /// </summary>
    public bool PlatformScope { get; init; }
}
