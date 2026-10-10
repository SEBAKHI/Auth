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
/// <param name="ClientId">
/// The <c>client_id</c> sent with the OAuth token endpoint's refresh grant; null
/// when it carried none, and always null from the first-party refresh endpoint.
/// When present it must name the application the token was issued to (RFC 6749
/// §6), or the request is refused before the token is touched. A match is also
/// what makes an application's token eligible for the application replay grace.
/// </param>
public record RefreshTokenCommand(
    string RefreshToken,
    string? IpAddress,
    string? UserAgent,
    bool ReplayGraceEligible = false,
    string? ClientId = null) : IRequest<ErrorOr<TokenResponse>>;
