namespace Auth.Application.DTOs;

/// <summary>
/// Response DTO for authentication token operations.
/// </summary>
public record TokenResponse : ISessionIssuingResponse
{
    TokenResponse? ISessionIssuingResponse.IssuedTokens => this;

    string? ISessionIssuingResponse.IssuedIdpSessionToken => null;

    ISessionIssuingResponse ISessionIssuingResponse.WithRefreshToken(string refreshToken) =>
        this with { RefreshToken = refreshToken };

    /// <summary>
    /// Gets the JWT access token.
    /// </summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// Gets the refresh token for obtaining new access tokens.
    /// </summary>
    public required string RefreshToken { get; init; }

    /// <summary>
    /// Gets the token type (always "Bearer").
    /// </summary>
    public string TokenType { get; init; } = "Bearer";

    /// <summary>
    /// Gets the access token expiration time in seconds.
    /// </summary>
    public required int ExpiresIn { get; init; }

    /// <summary>
    /// Gets the refresh token expiration time in seconds.
    /// </summary>
    public required int RefreshExpiresIn { get; init; }

    /// <summary>
    /// Gets the scopes granted to the application the tokens were issued to,
    /// space-delimited. Null for platform tokens, which have no application.
    /// </summary>
    public string? Scope { get; init; }
}
