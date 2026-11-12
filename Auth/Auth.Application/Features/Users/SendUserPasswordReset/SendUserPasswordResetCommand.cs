using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.SendUserPasswordReset;

/// <summary>
/// An administrator sends a password reset link to an account's own address (S13, D7): the
/// console's "send reset link" button, which used to call the anonymous forgot-password request
/// and so would have needed the sign-in page's bot check. The link goes to the account's address
/// only; the administrator never sees it.
/// </summary>
/// <param name="UserId">The account.</param>
/// <param name="RequestedBy">The administrator.</param>
public sealed record SendUserPasswordResetCommand(Guid UserId, Guid RequestedBy)
    : IRequest<ErrorOr<PasswordResetLinkResponse>>;
