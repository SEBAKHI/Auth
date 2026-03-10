using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.RefreshToken;

/// <summary>
/// Command to refresh an access token using a refresh token.
/// </summary>
public record RefreshTokenCommand(
    string RefreshToken,
    string? IpAddress,
    string? UserAgent) : IRequest<ErrorOr<TokenResponse>>;
