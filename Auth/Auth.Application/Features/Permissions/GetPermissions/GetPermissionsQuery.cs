using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.GetPermissions;

/// <summary>
/// Query to get permissions for an application.
/// </summary>
public record GetPermissionsQuery(Guid? ApplicationId = null) : IRequest<ErrorOr<IReadOnlyList<PermissionDto>>>;
