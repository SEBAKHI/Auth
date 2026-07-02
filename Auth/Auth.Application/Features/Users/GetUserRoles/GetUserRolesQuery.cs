using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUserRoles;

/// <summary>
/// Query to get all roles assigned to a user.
/// </summary>
public record GetUserRolesQuery(
    Guid UserId,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<UserRoleDto>>>;
