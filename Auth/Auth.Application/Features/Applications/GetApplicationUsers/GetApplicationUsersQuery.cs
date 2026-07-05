using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplicationUsers;

/// <summary>
/// Query to get paginated users under an application.
/// </summary>
public record GetApplicationUsersQuery(
    Guid ApplicationId,
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<PagedApplicationUsersDto>>;
