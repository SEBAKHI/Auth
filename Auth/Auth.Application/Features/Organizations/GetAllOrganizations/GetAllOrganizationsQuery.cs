using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetAllOrganizations;

/// <summary>
/// Query to get a paginated list of ALL organizations on the platform.
/// Platform administration — unlike GetUserOrganizationsQuery, results are
/// not scoped to the caller's memberships.
/// </summary>
public record GetAllOrganizationsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<PagedOrganizationsDto>>;
