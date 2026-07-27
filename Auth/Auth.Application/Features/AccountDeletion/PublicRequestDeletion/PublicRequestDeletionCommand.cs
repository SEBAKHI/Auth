using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.PublicRequestDeletion;

/// <summary>
/// Command for step 1 of the public no-login deletion flow: request a
/// verification code for an email address. Always acknowledges generically —
/// whether the account exists is never revealed.
/// </summary>
/// <param name="Email">The email address of the account to delete.</param>
public record PublicRequestDeletionCommand(string Email) : IRequest<ErrorOr<Success>>;
