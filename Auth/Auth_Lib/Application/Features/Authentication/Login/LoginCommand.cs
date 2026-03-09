using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Authentication.Login;

/// <summary>
/// Command to authenticate a user with email and password.
/// </summary>
public record LoginCommand(
    string Email,
    string Password,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId = null) : IRequest<ErrorOr<LoginResponse>>;
