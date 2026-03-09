using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Roles.GetRoles;

/// <summary>
/// Query to get roles for an application.
/// </summary>
public record GetRolesQuery(Guid? ApplicationId = null) : IRequest<ErrorOr<IReadOnlyList<RoleDto>>>;
