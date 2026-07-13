namespace Auth.Application.DTOs;

/// <summary>
/// Response DTO for login operations. When two-factor verification is pending,
/// <see cref="Token"/> and <see cref="User"/> are null and
/// <see cref="TwoFactorChallengeToken"/> carries the challenge to complete.
/// </summary>
public record LoginResponse
{
    /// <summary>
    /// Gets the token information (null while two-factor verification is pending).
    /// </summary>
    public TokenResponse? Token { get; init; }

    /// <summary>
    /// Gets the authenticated user information (null while two-factor verification is pending).
    /// </summary>
    public UserInfo? User { get; init; }

    /// <summary>
    /// Gets whether the user must change their password.
    /// </summary>
    public bool RequiresPasswordChange { get; init; }

    /// <summary>
    /// Gets whether two-factor verification is required to complete the login.
    /// </summary>
    public bool RequiresTwoFactor { get; init; }

    /// <summary>
    /// Gets the opaque challenge token to present to the two-factor verify
    /// endpoint (null unless <see cref="RequiresTwoFactor"/> is true).
    /// </summary>
    public string? TwoFactorChallengeToken { get; init; }
}
