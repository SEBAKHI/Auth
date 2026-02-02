using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.UserManagement.Queries;

/// <summary>
/// Query to get all roles assigned to a user.
/// </summary>
public record GetUserRolesQuery(Guid UserId) : IRequest<ErrorOr<IReadOnlyList<UserRoleDto>>>;
