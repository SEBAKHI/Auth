using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.GetRoleUsers;

/// <summary>
/// Query to get paginated users assigned a role.
/// </summary>
public record GetRoleUsersQuery(
    Guid RoleId,
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<PagedRoleUsersDto>>;
