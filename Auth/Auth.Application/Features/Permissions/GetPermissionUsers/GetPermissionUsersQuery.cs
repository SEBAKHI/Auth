using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.GetPermissionUsers;

/// <summary>
/// Query to get paginated users granted a permission.
/// </summary>
public record GetPermissionUsersQuery(
    Guid PermissionId,
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<PagedPermissionUsersDto>>;
