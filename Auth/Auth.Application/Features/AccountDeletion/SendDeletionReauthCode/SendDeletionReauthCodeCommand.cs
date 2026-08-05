using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.SendDeletionReauthCode;

/// <summary>
/// Command to email a deletion re-authentication code to the authenticated
/// user — step one of every in-app deletion request.
/// </summary>
/// <param name="UserId">The authenticated user.</param>
public record SendDeletionReauthCodeCommand(Guid UserId) : IRequest<ErrorOr<Success>>;
