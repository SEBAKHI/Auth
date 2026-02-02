using ErrorOr;
using MediatR;

namespace Auth_API.Modules.UserManagement.Commands;

/// <summary>
/// Command to remove a role from a user.
/// </summary>
public record RemoveUserRoleCommand(
    Guid UserId,
    Guid RoleId,
    Guid? ApplicationId = null) : IRequest<ErrorOr<bool>>
{
    /// <summary>
    /// The ID of the user removing this role (for audit).
    /// </summary>
    public Guid RemovedBy { get; set; }
}
