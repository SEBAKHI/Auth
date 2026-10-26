using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.SignInWithEmailCode;

/// <summary>
/// Signs in with the code a sign-in by emailed code sent (OI-215), and spends it.
/// </summary>
/// <param name="PendingId">The handle the start step answered with.</param>
/// <param name="Otp">The six-digit code that reached the address.</param>
/// <param name="RequireFullProof">
/// Add-only: the page asks that an account with a password prove it too, as the
/// "sign in again" dialog does before a security change. It can only make the sign-in
/// harder: a forged value asks for a password the forger must then know.
/// </param>
/// <param name="StaySignedIn">The person's "Stay signed in" choice (OI-154).</param>
/// <param name="DeviceId">The calling browser's own identifier, for the session.</param>
/// <param name="IpAddress">The caller's address as the gateway forwarded it.</param>
/// <param name="UserAgent">The caller's user agent.</param>
public record SignInWithEmailCodeCommand(
    string PendingId,
    string Otp,
    bool RequireFullProof,
    bool StaySignedIn,
    string? DeviceId,
    string? IpAddress,
    string? UserAgent) : IRequest<ErrorOr<LoginResponse>>;
