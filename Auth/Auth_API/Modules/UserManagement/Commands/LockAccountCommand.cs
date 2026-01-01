using ErrorOr;
using MediatR;

namespace Auth_API.Modules.UserManagement.Commands;

/// <summary>
/// Command to lock a user account.
/// </summary>
/// <param name="UserId">The ID of the user to lock.</param>
/// <param name="Reason">The reason for locking the account.</param>
/// <param name="LockDurationMinutes">Duration of the lock in minutes. Null for indefinite lock.</param>
/// <param name="LockedBy">The ID of the user performing the lock action.</param>
public record LockAccountCommand(
    Guid UserId,
    string Reason,
    int? LockDurationMinutes,
    Guid LockedBy
) : IRequest<ErrorOr<Success>>;
