using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.AssignRole;

/// <summary>
/// Command to assign a role to a user.
/// </summary>
public record AssignRoleCommand(
    Guid UserId,
    Guid RoleId,
    DateTime? ExpiresAt = null,
    Guid? ApplicationId = null) : IRequest<ErrorOr<Success>>
{
    /// <summary>
    /// The ID of the user assigning the role (for audit).
    /// </summary>
    public Guid AssignedBy { get; init; }

    // ApplicationId scopes the assignment to one application; null keeps it
    // platform-wide. Until this existed the column was unreachable from any API
    // path, so an application-scoped role could not be granted at all.
}
