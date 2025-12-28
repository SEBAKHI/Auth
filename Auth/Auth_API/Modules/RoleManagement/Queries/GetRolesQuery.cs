using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.RoleManagement.Queries;

/// <summary>
/// Query to get roles for an application.
/// </summary>
public record GetRolesQuery(Guid? ApplicationId = null) : IRequest<ErrorOr<IReadOnlyList<RoleDto>>>;
