using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for user login.
/// </summary>
public record LoginRequest
{
    /// <summary>
    /// Gets the user's email address.
    /// </summary>
    [Required]
    [EmailAddress]
    public required string Email { get; init; }

    /// <summary>
    /// Gets the user's password.
    /// </summary>
    [Required]
    public required string Password { get; init; }

    /// <summary>
    /// Gets the optional device identifier for session management.
    /// </summary>
    public string? DeviceId { get; init; }
}
