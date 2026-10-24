using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.GetRoleById;

/// <summary>
/// Query to get a role by ID.
/// </summary>
public record GetRoleByIdQuery(Guid Id) : IRequest<ErrorOr<RoleDto>>
{
    /// <summary>
    /// The caller, for the role's <see cref="RoleDto.RemovablePermissions"/>; null computes none, which a client reads
    /// as every code removable.
    /// </summary>
    public Guid? ActorId { get; init; }
}
