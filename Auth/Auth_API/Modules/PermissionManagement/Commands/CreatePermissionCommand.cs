using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.PermissionManagement.Commands;

/// <summary>
/// Command to create a new permission.
/// </summary>
public record CreatePermissionCommand(
    Guid ApplicationId,
    string Code,
    string Name,
    string? Description = null,
    Guid? ParentId = null) : IRequest<ErrorOr<PermissionDto>>
{
    /// <summary>
    /// The ID of the user creating this permission (for audit).
    /// </summary>
    public Guid CreatedBy { get; set; }
}
