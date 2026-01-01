using ErrorOr;
using MediatR;

namespace Auth_API.Modules.UserManagement.Commands;

/// <summary>
/// Command to unlock a user account.
/// </summary>
/// <param name="UserId">The ID of the user to unlock.</param>
/// <param name="UnlockedBy">The ID of the user performing the unlock action.</param>
public record UnlockAccountCommand(
    Guid UserId,
    Guid UnlockedBy
) : IRequest<ErrorOr<Success>>;
