using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.PermissionManagement.Commands;

/// <summary>
/// Command to update an existing permission.
/// </summary>
public record UpdatePermissionCommand(
    Guid Id,
    string Name,
    string? Description = null) : IRequest<ErrorOr<PermissionDto>>
{
    /// <summary>
    /// The ID of the user modifying this permission (for audit).
    /// </summary>
    public Guid ModifiedBy { get; set; }
}
