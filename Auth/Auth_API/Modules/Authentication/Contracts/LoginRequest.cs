namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for user login.
/// </summary>
public record LoginRequest
{
    /// <summary>
    /// Gets the user's email address.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Gets the user's password.
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional device identifier for session management.
    /// </summary>
    public string? DeviceId { get; init; }
}
