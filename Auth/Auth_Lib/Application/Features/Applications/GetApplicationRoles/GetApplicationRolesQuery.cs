using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Applications.GetApplicationRoles;

/// <summary>
/// Query to get all roles for an application.
/// </summary>
public record GetApplicationRolesQuery(Guid ApplicationId) : IRequest<ErrorOr<IReadOnlyList<RoleDto>>>;
