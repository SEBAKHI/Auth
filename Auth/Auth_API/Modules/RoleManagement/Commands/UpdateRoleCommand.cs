using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.RoleManagement.Commands;

/// <summary>
/// Command to update an existing role.
/// </summary>
public record UpdateRoleCommand(
    Guid Id,
    string Name,
    string? Description = null) : IRequest<ErrorOr<RoleDto>>
{
    /// <summary>
    /// The ID of the user updating this role (for audit).
    /// </summary>
    public Guid ModifiedBy { get; set; }
}
