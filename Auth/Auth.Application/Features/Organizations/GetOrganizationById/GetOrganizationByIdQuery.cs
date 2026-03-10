using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetOrganizationById;

/// <summary>
/// Query to get organization details by ID.
/// </summary>
public record GetOrganizationByIdQuery(Guid OrganizationId) : IRequest<ErrorOr<OrganizationDetailDto>>
{
    /// <summary>
    /// The ID of the user making the request.
    /// Used to verify access to the organization.
    /// </summary>
    public Guid RequestedBy { get; set; }
}
