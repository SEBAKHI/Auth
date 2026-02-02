using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.UserManagement.Queries;

/// <summary>
/// Query to get all direct permissions granted to a user.
/// </summary>
public record GetUserPermissionsQuery(Guid UserId) : IRequest<ErrorOr<IReadOnlyList<UserPermissionDto>>>;
