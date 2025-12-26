namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for user logout.
/// </summary>
public record LogoutRequest
{
    /// <summary>
    /// Gets the refresh token to revoke.
    /// </summary>
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Gets whether to logout from all devices.
    /// </summary>
    public bool LogoutAllDevices { get; init; }
}
