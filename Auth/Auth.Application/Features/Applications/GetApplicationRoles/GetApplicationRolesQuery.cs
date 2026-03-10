using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplicationRoles;

/// <summary>
/// Query to get all roles for an application.
/// </summary>
public record GetApplicationRolesQuery(Guid ApplicationId) : IRequest<ErrorOr<IReadOnlyList<RoleDto>>>;
