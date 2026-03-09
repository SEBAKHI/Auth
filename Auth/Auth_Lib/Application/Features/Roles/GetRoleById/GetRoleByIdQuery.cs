using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Roles.GetRoleById;

/// <summary>
/// Query to get a role by ID.
/// </summary>
public record GetRoleByIdQuery(Guid Id) : IRequest<ErrorOr<RoleDto>>;
