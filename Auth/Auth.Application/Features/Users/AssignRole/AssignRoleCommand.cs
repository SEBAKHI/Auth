using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.AssignRole;

/// <summary>
/// Command to assign a role to a user.
/// </summary>
public record AssignRoleCommand(
    Guid UserId,
    Guid RoleId,
    DateTime? ExpiresAt = null) : IRequest<ErrorOr<Success>>
{
    /// <summary>
    /// The ID of the user assigning the role (for audit).
    /// </summary>
    public Guid AssignedBy { get; set; }
}
