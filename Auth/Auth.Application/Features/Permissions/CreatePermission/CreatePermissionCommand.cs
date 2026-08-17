using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.CreatePermission;

/// <summary>
/// Command to create a new permission.
/// </summary>
/// <remarks>
/// <see cref="ApplicationId"/> is optional, and null is the platform's own
/// scope. Every permission this system enforces on itself is seeded that way -
/// users:read, roles:read, secrets.manage and the rest all carry a null
/// ApplicationId - yet this command demanded one, so the console could define a
/// permission for a registered application and could not define one for the
/// platform. The domain entity always allowed it; only this contract did not.
/// </remarks>
public record CreatePermissionCommand(
    Guid? ApplicationId,
    string Code,
    string Name,
    string? Description = null,
    Guid? ParentId = null) : IRequest<ErrorOr<PermissionDto>>
{
    /// <summary>
    /// The ID of the user creating this permission (for audit).
    /// </summary>
    public Guid CreatedBy { get; init; }
}
