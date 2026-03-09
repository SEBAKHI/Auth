using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Applications.GetApplications;

/// <summary>
/// Query to get a paginated list of applications.
/// </summary>
public record GetApplicationsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    bool? IsActive = null) : IRequest<ErrorOr<PagedApplicationsDto>>;
