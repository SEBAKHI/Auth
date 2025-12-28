using ErrorOr;
using MediatR;

namespace Auth_API.Modules.UserManagement.Commands;

/// <summary>
/// Command to delete a user.
/// </summary>
public record DeleteUserCommand(Guid Id) : IRequest<ErrorOr<Success>>
{
    /// <summary>
    /// The ID of the user performing the deletion (for audit).
    /// </summary>
    public Guid DeletedBy { get; set; }
}
