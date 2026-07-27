using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ExternalLogin;

/// <summary>
/// Command to authenticate a user via an external provider (Google, Apple, etc.).
/// If the user doesn't exist, creates a new account. If they do, logs them in.
/// </summary>
/// <param name="AuthorizationCode">
/// Single-use code from the sign-in (Apple), exchanged server-side for the
/// revocable refresh token stored for deletion-time revocation.
/// </param>
/// <param name="GivenName">
/// Client-supplied first name — Apple sends the name only on the first
/// authorization and never inside the ID token; used solely at first
/// registration.
/// </param>
/// <param name="FamilyName">Client-supplied last name (see <paramref name="GivenName"/>).</param>
public record ExternalLoginCommand(
    string Provider,
    string IdToken,
    string? Nonce,
    bool CreateOrganization = false,
    string? IpAddress = null,
    string? UserAgent = null,
    string? DeviceId = null,
    string? AuthorizationCode = null,
    string? GivenName = null,
    string? FamilyName = null) : IRequest<ErrorOr<LoginResponse>>;
