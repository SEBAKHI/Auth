using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.RecoverAccountWithEmailCode;

/// <summary>
/// Recovers an account waiting for deletion with the code a sign-in by emailed code
/// sent to it (OI-215): the third recovery entry, beside the password and the
/// provider, and the only one an account without a password and without a provider
/// has. Success cancels the deletion, restores the account and signs it in.
/// </summary>
/// <param name="PendingId">The handle the start step answered with.</param>
/// <param name="Otp">The six-digit code that reached the address.</param>
/// <param name="TwoFactorCode">The authenticator app's code or a recovery code, when the account has the app.</param>
/// <param name="RequireFullProof">The page's add-only reason, as at sign-in.</param>
/// <param name="IpAddress">The caller's address as the gateway forwarded it.</param>
/// <param name="UserAgent">The caller's user agent.</param>
/// <param name="DeviceId">The calling browser's own identifier, for the session.</param>
public record RecoverAccountWithEmailCodeCommand(
    string PendingId,
    string Otp,
    string? TwoFactorCode,
    bool RequireFullProof,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId) : IRequest<ErrorOr<LoginResponse>>;
