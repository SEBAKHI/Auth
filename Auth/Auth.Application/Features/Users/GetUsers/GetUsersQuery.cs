using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUsers;

/// <summary>
/// Query to get a paginated list of users.
/// </summary>
public record GetUsersQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null) : IRequest<ErrorOr<PagedUsersDto>>;
