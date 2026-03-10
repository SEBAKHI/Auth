using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.DeactivateAccount;

/// <summary>
/// Command to deactivate a user account.
/// </summary>
/// <param name="UserId">The ID of the user to deactivate.</param>
/// <param name="DeactivatedBy">The ID of the user performing the deactivation.</param>
public record DeactivateAccountCommand(
    Guid UserId,
    Guid DeactivatedBy
) : IRequest<ErrorOr<Success>>;
