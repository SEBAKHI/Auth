using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplicationOrganizations;

/// <summary>
/// Query to get paginated organizations that have an application enabled.
/// </summary>
public record GetApplicationOrganizationsQuery(
    Guid ApplicationId,
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<PagedApplicationOrganizationsDto>>;
