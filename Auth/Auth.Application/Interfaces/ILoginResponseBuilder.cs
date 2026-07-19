using Auth.Application.DTOs;
using Auth.Domain.Entities;

namespace Auth.Application.Interfaces;

/// <summary>
/// Shared service that builds a LoginResponse with tokens and user info.
/// Used by both LoginCommandHandler and ExternalLoginCommandHandler.
/// </summary>
public interface ILoginResponseBuilder
{
    /// <summary>
    /// Builds a complete login response: generates tokens, stores refresh token, records login attempt.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="ipAddress">The client's IP address.</param>
    /// <param name="deviceInfo">The client's device info (user agent + device ID).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="establishIdpSession">
    /// Whether to also mint an IdP SSO session (carried on
    /// <see cref="LoginResponse.IdpSessionToken"/> for the controller to move
    /// into the session cookie). Interactive logins pass true; the OAuth token
    /// endpoint passes false — no browser is present there to receive a cookie.
    /// </param>
    /// <returns>The login response with tokens and user info.</returns>
    Task<LoginResponse> BuildAsync(User user, string? ipAddress, string? deviceInfo, CancellationToken cancellationToken, bool establishIdpSession = true);
}
