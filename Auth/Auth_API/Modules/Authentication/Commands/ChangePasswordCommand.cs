using ErrorOr;
using MediatR;

namespace Auth_API.Modules.Authentication.Commands;

/// <summary>
/// Command to change a user's password.
/// </summary>
/// <param name="UserId">The ID of the user changing their password.</param>
/// <param name="CurrentPassword">The user's current password for verification.</param>
/// <param name="NewPassword">The new password to set.</param>
public record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword) : IRequest<ErrorOr<Success>>;
