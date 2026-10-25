using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.EnableEmailCode;

/// <summary>
/// Command to start making the emailed code the account's second factor (S24): emails
/// a code to the account's address, which <c>ConfirmEmailCodeCommand</c> then checks.
/// </summary>
/// <param name="UserId">The signed-in account.</param>
/// <param name="CurrentSessionId">
/// The session the access token belongs to (its <c>sid</c>), measured by the
/// re-authentication check.
/// </param>
/// <param name="IpAddress">The caller's address, named in the email.</param>
public record EnableEmailCodeCommand(
    Guid UserId,
    Guid? CurrentSessionId,
    string? IpAddress) : IRequest<ErrorOr<EmailCodeSent>>;
