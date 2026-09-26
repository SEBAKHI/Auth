namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for token refresh.
/// </summary>
public record RefreshTokenRequest
{
    /// <summary>
    /// Gets the refresh token.
    /// </summary>
    public string RefreshToken { get; init; } = string.Empty;
}
