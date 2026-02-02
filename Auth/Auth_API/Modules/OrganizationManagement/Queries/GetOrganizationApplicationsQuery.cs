using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.OrganizationManagement.Queries;

/// <summary>
/// Query to get all enabled applications for an organization.
/// </summary>
public record GetOrganizationApplicationsQuery(Guid OrganizationId) : IRequest<ErrorOr<IReadOnlyList<OrganizationApplicationDto>>>
{
    /// <summary>
    /// The user making the request (for authorization).
    /// </summary>
    public Guid RequestedBy { get; init; }
}
