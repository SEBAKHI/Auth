using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUserRoles;

/// <summary>
/// Query to get all roles assigned to a user.
/// </summary>
public record GetUserRolesQuery(Guid UserId) : IRequest<ErrorOr<IReadOnlyList<UserRoleDto>>>;
