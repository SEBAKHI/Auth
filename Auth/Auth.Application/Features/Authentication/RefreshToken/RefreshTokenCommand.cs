using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.RefreshToken;

/// <summary>
/// Command to refresh an access token using a refresh token.
/// </summary>
/// <param name="RefreshToken">The presented refresh token.</param>
/// <param name="IpAddress">The caller's address, for logging and the new token row.</param>
/// <param name="UserAgent">The caller's user agent.</param>
/// <param name="ReplayGraceEligible">
/// True only when the token came from the first-party refresh cookie, which no
/// script can read. Such a token, presented again within the replay grace window
/// right after its rotation, is answered once more instead of being treated as
/// theft: the browser cannot tell that a rotation response was lost. A token sent
/// in a request body is never eligible.
/// </param>
public record RefreshTokenCommand(
    string RefreshToken,
    string? IpAddress,
    string? UserAgent,
    bool ReplayGraceEligible = false) : IRequest<ErrorOr<TokenResponse>>;
