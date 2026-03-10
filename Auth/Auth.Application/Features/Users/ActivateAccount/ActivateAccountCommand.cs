using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.ActivateAccount;

/// <summary>
/// Command to activate a user account.
/// </summary>
/// <param name="UserId">The ID of the user to activate.</param>
/// <param name="ActivatedBy">The ID of the user performing the activation.</param>
public record ActivateAccountCommand(
    Guid UserId,
    Guid ActivatedBy
) : IRequest<ErrorOr<Success>>;
