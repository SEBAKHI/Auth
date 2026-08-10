using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.RecoverAccountExternal;

/// <summary>
/// Command to recover an account pending deletion during its grace window,
/// authenticated by an external identity provider's ID token (and TOTP when
/// 2FA is enabled). Success cancels the deletion, restores the account and
/// signs the user in.
/// </summary>
public record RecoverAccountExternalCommand(
    string Provider,
    string IdToken,
    string? Nonce,
    string? TwoFactorCode,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId = null) : IRequest<ErrorOr<LoginResponse>>;
