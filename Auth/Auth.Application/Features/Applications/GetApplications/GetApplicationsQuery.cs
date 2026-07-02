using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplications;

/// <summary>
/// Query to get a paginated list of applications.
/// </summary>
public record GetApplicationsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    bool? IsActive = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<PagedApplicationsDto>>;
