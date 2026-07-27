using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.RecoverAccount;

/// <summary>
/// Command to recover an account pending deletion during its grace window,
/// authenticated by password (and TOTP when 2FA is enabled). Success cancels
/// the deletion, restores the account and signs the user in.
/// </summary>
public record RecoverAccountCommand(
    string Email,
    string Password,
    string? TwoFactorCode,
    string? IpAddress,
    string? UserAgent) : IRequest<ErrorOr<LoginResponse>>;
