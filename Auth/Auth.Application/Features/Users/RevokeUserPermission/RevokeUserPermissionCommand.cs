using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.RevokeUserPermission;

/// <summary>
/// Command to revoke a direct permission from a user.
/// </summary>
public record RevokeUserPermissionCommand(
    Guid UserId,
    Guid PermissionId,
    Guid? ApplicationId = null) : IRequest<ErrorOr<bool>>
{
    /// <summary>
    /// The ID of the user revoking this permission (for audit).
    /// </summary>
    public Guid RevokedBy { get; init; }
}
