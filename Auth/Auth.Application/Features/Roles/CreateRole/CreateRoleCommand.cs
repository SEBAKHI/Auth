using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.CreateRole;

/// <summary>
/// Command to create a new role.
/// </summary>
/// <remarks>
/// <see cref="ApplicationId"/> is optional, and null is the platform's own
/// scope — where every seeded role lives (super-admin, admin, user-manager,
/// auditor, user). Demanding one meant the console could define a role for a
/// registered application and not one for the platform, which is the only scope
/// that exists until an application is registered. The domain entity always
/// allowed it.
/// </remarks>
public record CreateRoleCommand(
    Guid? ApplicationId,
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
