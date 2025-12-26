namespace Auth_Lib.DTOs;

/// <summary>
/// Response DTO for successful login operations.
/// </summary>
public record LoginResponse
{
    /// <summary>
    /// Gets the token information.
    /// </summary>
    public required TokenResponse Token { get; init; }

    /// <summary>
    /// Gets the authenticated user information.
    /// </summary>
    public required UserInfo User { get; init; }

    /// <summary>
    /// Gets whether the user must change their password.
    /// </summary>
    public bool RequiresPasswordChange { get; init; }

    /// <summary>
    /// Gets whether two-factor authentication is required.
    /// </summary>
    public bool RequiresTwoFactor { get; init; }
}
