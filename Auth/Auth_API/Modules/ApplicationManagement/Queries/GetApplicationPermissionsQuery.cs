using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.ApplicationManagement.Queries;

/// <summary>
/// Query to get all permissions for an application.
/// </summary>
public record GetApplicationPermissionsQuery(Guid ApplicationId) : IRequest<ErrorOr<IReadOnlyList<PermissionDto>>>;
