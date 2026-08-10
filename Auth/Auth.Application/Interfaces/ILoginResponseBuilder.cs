using Auth.Application.DTOs;
using Auth.Domain.Entities;
using ErrorOr;

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
    /// <param name="userAgent">The client's raw user-agent header, if it sent one.</param>
    /// <param name="deviceId">
    /// The client-supplied per-browser identifier, if it sent one. Recognition
    /// only — it is forgeable and is never read as an authorization input. Null
    /// for callers that have no browser storage to keep it in, such as the OAuth
    /// token endpoint.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="establishIdpSession">
    /// Whether to also mint an IdP SSO session (carried on
    /// <see cref="LoginResponse.IdpSessionToken"/> for the controller to move
    /// into the session cookie). Interactive logins pass true; the OAuth token
    /// endpoint passes false — no browser is present there to receive a cookie.
    /// </param>
    /// <param name="audience">
    /// The access token's audience (the requesting app's client id for the OAuth
    /// flow); null uses the platform default. Also recorded on the refresh token
    /// (via <paramref name="applicationId"/>) so refreshes keep the same audience.
    /// </param>
    /// <param name="applicationId">The application the tokens are scoped to, if any.</param>
    /// <returns>
    /// The login response with tokens and user info, or
    /// <c>Session.MaxSessionsReached</c> when the account is at its concurrent
    /// session limit and the limit is configured to refuse rather than evict.
    /// That refusal is the reason this returns <see cref="ErrorOr{T}"/> at all:
    /// it has to be decided before a single token is minted, which no caller can
    /// do on its own without duplicating the check six times.
    /// </returns>
    Task<ErrorOr<LoginResponse>> BuildAsync(
        User user,
        string? ipAddress,
        string? userAgent,
        string? deviceId,
        CancellationToken cancellationToken,
        bool establishIdpSession = true,
        string? audience = null,
        Guid? applicationId = null);
}
