using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.RevokeRolePermission;

/// <summary>
/// Command to remove a permission from an existing role.
/// </summary>
public record RevokeRolePermissionCommand(
    Guid RoleId,
    Guid PermissionId) : IRequest<ErrorOr<Success>>
{
    /// <summary>The user making the change (for audit).</summary>
    public Guid RevokedBy { get; init; }
}
