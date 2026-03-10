using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUserPermissions;

/// <summary>
/// Query to get all direct permissions granted to a user.
/// </summary>
public record GetUserPermissionsQuery(Guid UserId) : IRequest<ErrorOr<IReadOnlyList<UserPermissionDto>>>;
