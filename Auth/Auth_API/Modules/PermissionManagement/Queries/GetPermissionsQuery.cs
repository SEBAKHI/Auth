using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.PermissionManagement.Queries;

/// <summary>
/// Query to get permissions for an application.
/// </summary>
public record GetPermissionsQuery(Guid? ApplicationId = null) : IRequest<ErrorOr<IReadOnlyList<PermissionDto>>>;
