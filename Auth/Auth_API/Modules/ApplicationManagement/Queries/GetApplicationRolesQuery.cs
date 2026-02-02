using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.ApplicationManagement.Queries;

/// <summary>
/// Query to get all roles for an application.
/// </summary>
public record GetApplicationRolesQuery(Guid ApplicationId) : IRequest<ErrorOr<IReadOnlyList<RoleDto>>>;
