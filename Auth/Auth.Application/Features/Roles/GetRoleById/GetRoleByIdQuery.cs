using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.GetRoleById;

/// <summary>
/// Query to get a role by ID.
/// </summary>
public record GetRoleByIdQuery(Guid Id) : IRequest<ErrorOr<RoleDto>>;
