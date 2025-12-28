using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.RoleManagement.Queries;

/// <summary>
/// Query to get a role by ID.
/// </summary>
public record GetRoleByIdQuery(Guid Id) : IRequest<ErrorOr<RoleDto>>;
