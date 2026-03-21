using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.CreateRole;

/// <summary>
/// Command to create a new role.
/// </summary>
public record CreateRoleCommand(
    Guid ApplicationId,
    string Code,
    string Name,
    string? Description = null,
    IReadOnlyList<Guid>? PermissionIds = null) : IRequest<ErrorOr<RoleDto>>
{
    /// <summary>
    /// The ID of the user creating this role (for audit).
    /// </summary>
    public Guid CreatedBy { get; init; }
}
