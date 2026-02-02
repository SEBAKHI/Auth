using ErrorOr;
using MediatR;

namespace Auth_API.Modules.PermissionManagement.Commands;

/// <summary>
/// Command to delete a permission.
/// </summary>
public record DeletePermissionCommand(Guid Id) : IRequest<ErrorOr<bool>>
{
    /// <summary>
    /// The ID of the user deleting this permission (for audit).
    /// </summary>
    public Guid DeletedBy { get; set; }
}
