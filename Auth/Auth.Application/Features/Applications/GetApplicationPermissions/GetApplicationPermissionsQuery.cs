using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplicationPermissions;

/// <summary>
/// Query to get all permissions for an application.
/// </summary>
public record GetApplicationPermissionsQuery(Guid ApplicationId) : IRequest<ErrorOr<IReadOnlyList<PermissionDto>>>;
