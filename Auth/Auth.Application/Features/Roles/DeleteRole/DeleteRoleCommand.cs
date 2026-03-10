using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.DeleteRole;

/// <summary>
/// Command to delete a role.
/// </summary>
public record DeleteRoleCommand(Guid Id) : IRequest<ErrorOr<Success>>
{
    /// <summary>
    /// The ID of the user deleting this role (for audit).
    /// </summary>
    public Guid DeletedBy { get; set; }
}
