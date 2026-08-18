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
/// <param name="NonceCookie">
/// The hash this server stored when it issued <paramref name="Nonce"/>. This
/// endpoint takes a provider token from an anonymous caller exactly as sign-in
/// does, so it is held to the same rule — guarding only one of the two would
/// move the way in rather than close it.
/// </param>
public record RecoverAccountExternalCommand(
    string Provider,
    string IdToken,
    string? Nonce,
    string? TwoFactorCode,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId = null,
    string? NonceCookie = null) : IRequest<ErrorOr<LoginResponse>>;
