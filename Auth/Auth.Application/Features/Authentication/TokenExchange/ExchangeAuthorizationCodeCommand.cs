using System.Text.Json.Serialization;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.TokenExchange;

/// <summary>
/// OAuth 2.0 token request for the authorization_code grant (RFC 6749 §4.1.3)
/// with mandatory PKCE verification (RFC 7636).
/// </summary>
/// <param name="Code">The one-time authorization code.</param>
/// <param name="RedirectUri">Must equal the redirect_uri the code was issued for.</param>
/// <param name="ClientId">The application code acting as the public client id.</param>
/// <param name="CodeVerifier">The PKCE code verifier.</param>
/// <param name="IpAddress">The client's IP address.</param>
/// <param name="UserAgent">The client's user agent.</param>
/// <param name="DeviceId">
/// The calling browser's identifier, when one called. A confidential client
/// exchanging a code server-side sends none, and the session is recorded without
/// a browser — but a public SPA completing PKCE in the browser does, and its
/// session belongs under the same browser as the rest of that user's.
/// </param>
public record ExchangeAuthorizationCodeCommand(
    string? Code,
    string? RedirectUri,
    string? ClientId,
    string? CodeVerifier,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId = null) : IRequest<ErrorOr<OAuthTokenResponse>>;

/// <summary>
/// OAuth 2.0 token endpoint response (RFC 6749 §5.1). Property names are pinned
/// to the spec's snake_case so standard OAuth clients can parse it.
/// </summary>
public record OAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; init; }

    [JsonPropertyName("refresh_expires_in")]
    public required int RefreshExpiresIn { get; init; }
}
