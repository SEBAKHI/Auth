using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Users.GrantUserPermission;

/// <summary>
/// Command to grant a direct permission to a user.
/// </summary>
public record GrantUserPermissionCommand(
    Guid UserId,
    Guid PermissionId,
    Guid? ApplicationId = null,
    DateTime? ExpiresAt = null) : IRequest<ErrorOr<bool>>
{
    /// <summary>
    /// The ID of the user granting this permission (for audit).
    /// </summary>
    public Guid GrantedBy { get; set; }
}
