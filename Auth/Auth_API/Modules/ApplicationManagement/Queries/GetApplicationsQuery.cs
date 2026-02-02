using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.ApplicationManagement.Queries;

/// <summary>
/// Query to get a paginated list of applications.
/// </summary>
public record GetApplicationsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    bool? IsActive = null) : IRequest<ErrorOr<PagedApplicationsDto>>;
