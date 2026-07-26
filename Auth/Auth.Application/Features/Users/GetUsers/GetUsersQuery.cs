using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUsers;

/// <summary>
/// Query to get a paginated list of users. <see cref="IncludeDeleted"/> widens
/// the result to soft-deleted accounts; the endpoint only sets it for callers
/// holding platform user management permission.
/// </summary>
public record GetUsersQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc,
    bool IncludeDeleted = false) : IRequest<ErrorOr<PagedUsersDto>>;
