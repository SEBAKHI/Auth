using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.GrantRolePermission;

/// <summary>
/// Command to add a permission to an existing role.
/// </summary>
/// <remarks>
/// A role's permission set used to be writable only while creating it, and the
/// console never sent one — so the set was fixed at creation and, in practice,
/// fixed forever. Changing what a role could do meant editing RolePermissions
/// by hand in the database.
/// </remarks>
public record GrantRolePermissionCommand(
    Guid RoleId,
    Guid PermissionId) : IRequest<ErrorOr<Success>>
{
    /// <summary>The user making the change (for audit).</summary>
    public Guid GrantedBy { get; init; }
}
