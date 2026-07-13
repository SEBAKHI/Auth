using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetOrganizationApplications;

/// <summary>
/// Query to get all enabled applications for an organization.
/// </summary>
public record GetOrganizationApplicationsQuery(
    Guid OrganizationId,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<OrganizationApplicationDto>>>
{
    /// <summary>
    /// The user making the request (for authorization).
    /// </summary>
    public Guid RequestedBy { get; init; }

    /// <summary>
    /// True when the caller holds the platform-wide organizations permission —
    /// skips the membership check. Set by the controller from JWT claims only,
    /// never bound from the request.
    /// </summary>
    public bool PlatformScope { get; init; }
}
