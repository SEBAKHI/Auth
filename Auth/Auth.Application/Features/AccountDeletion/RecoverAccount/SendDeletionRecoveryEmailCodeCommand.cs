using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.RecoverAccount;

/// <summary>
/// Command to email the code that recovers an account pending deletion whose second
/// factor is the emailed code (S24), on the password entry. The same credentials as
/// <see cref="RecoverAccountCommand"/>, checked the same way; nothing is recovered,
/// the code is sent.
/// </summary>
public record SendDeletionRecoveryEmailCodeCommand(
    string Email,
    string Password,
    string? IpAddress,
    string? UserAgent) : IRequest<ErrorOr<EmailCodeSent>>;
