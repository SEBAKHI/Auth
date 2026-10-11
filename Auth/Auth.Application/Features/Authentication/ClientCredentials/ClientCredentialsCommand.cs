using System.Text.Json.Serialization;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ClientCredentials;

/// <summary>
/// OAuth 2.0 client credentials grant (RFC 6749 §4.4) for a machine client, authenticated with
/// <c>private_key_jwt</c> (RFC 7523 §2.2, OIDC Core §9): the application's own server proves who it
/// is by a short statement signed with its machine key (OI-128).
/// </summary>
/// <param name="ClientAssertionType">Must be <c>urn:ietf:params:oauth:client-assertion-type:jwt-bearer</c>.</param>
/// <param name="ClientAssertion">The signed assertion.</param>
/// <param name="ClientId">Optional; when sent, it must name the same application as the assertion.</param>
/// <param name="Scope">Optional, space-delimited; absent means every machine scope.</param>
/// <param name="IpAddress">The caller's address, for the log.</param>
public record ClientCredentialsCommand(
    string? ClientAssertionType,
    string? ClientAssertion,
    string? ClientId,
    string? Scope,
    string? IpAddress) : IRequest<ErrorOr<ClientCredentialsTokenResponse>>;

/// <summary>
/// The token response of the client credentials grant (RFC 6749 §4.4.3, §5.1): an access token and
/// nothing to renew it with. No refresh token (§4.4.3: one SHOULD NOT be issued) and no id token
/// (no user signed in).
/// </summary>
public record ClientCredentialsTokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    /// <summary>The machine scopes granted, space-delimited.</summary>
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }
}
