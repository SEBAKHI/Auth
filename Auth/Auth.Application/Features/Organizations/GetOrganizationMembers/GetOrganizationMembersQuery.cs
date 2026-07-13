using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetOrganizationMembers;

/// <summary>
/// Query to get paginated members of an organization.
/// </summary>
public record GetOrganizationMembersQuery(
    Guid OrganizationId,
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<PagedOrganizationMembersDto>>
{
    /// <summary>
    /// The ID of the user making the request.
    /// </summary>
    public Guid RequestedBy { get; set; }

    /// <summary>
    /// True when the caller holds the platform-wide organizations permission —
    /// skips the membership check. Set by the controller from JWT claims only,
    /// never bound from the request.
    /// </summary>
    public bool PlatformScope { get; init; }
}
