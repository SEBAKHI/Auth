using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Authentication.RefreshToken;

/// <summary>
/// Command to refresh an access token using a refresh token.
/// </summary>
public record RefreshTokenCommand(
    string RefreshToken,
    string? IpAddress,
    string? UserAgent) : IRequest<ErrorOr<TokenResponse>>;
