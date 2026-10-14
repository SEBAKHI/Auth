using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.Login;

/// <summary>
/// Command to authenticate a user with email and password.
/// </summary>
/// <param name="StaySignedIn">
/// The person ticked "Stay signed in" (OI-154). Absent means a browser session.
/// </param>
public record LoginCommand(
    string Email,
    string Password,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId = null,
    bool StaySignedIn = false) : IRequest<ErrorOr<LoginResponse>>;
