using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Users.GetUserRoles;

/// <summary>
/// Query to get all roles assigned to a user.
/// </summary>
public record GetUserRolesQuery(Guid UserId) : IRequest<ErrorOr<IReadOnlyList<UserRoleDto>>>;
