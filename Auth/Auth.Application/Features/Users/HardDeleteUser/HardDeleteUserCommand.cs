using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.HardDeleteUser;

/// <summary>
/// Command to permanently remove a soft-deleted user together with all of
/// their dependent records (sessions, tokens, assignments, memberships,
/// notifications and audit trail). Irreversible; used to clean experimental
/// accounts out of the database.
/// </summary>
public record HardDeleteUserCommand(Guid Id) : IRequest<ErrorOr<Success>>
{
    /// <summary>
    /// The ID of the administrator performing the permanent deletion (for audit).
    /// </summary>
    public Guid DeletedBy { get; init; }
}
