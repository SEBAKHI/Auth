using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.Logout;

/// <summary>
/// Command to logout a user and revoke their tokens.
/// </summary>
public record LogoutCommand(
    Guid UserId,
    string? RefreshToken,
    string? AccessToken,
    string? IpAddress,
    bool LogoutAllDevices = false) : IRequest<ErrorOr<Success>>;
