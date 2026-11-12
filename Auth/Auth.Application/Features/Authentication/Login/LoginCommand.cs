using Auth.Application.Common.BotDefense;
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
/// <param name="BotToken">The page's bot-check token (S13), or null when the request carried none.</param>
public record LoginCommand(
    string Email,
    string Password,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId = null,
    bool StaySignedIn = false,
    string? BotToken = null) : IRequest<ErrorOr<LoginResponse>>, IBotProtectedRequest
{
    string IBotProtectedRequest.Action => BotDefenseActions.Login;

    string? IBotProtectedRequest.ClientIp => IpAddress;
}
