using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for public user registration.
/// </summary>
public record RegisterRequest
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
    /// Gets the user's first name.
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string FirstName { get; init; }

    /// <summary>
    /// Gets the user's last name.
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string LastName { get; init; }

    /// <summary>
    /// Gets the optional display name.
    /// </summary>
    [StringLength(200)]
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the optional phone number.
    /// </summary>
    [StringLength(20)]
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// Gets the optional preferred language code (e.g., "en", "ar").
    /// </summary>
    [StringLength(10)]
    public string? PreferredLanguage { get; init; }

    /// <summary>
    /// Gets the optional timezone identifier (e.g., "UTC", "America/New_York").
    /// </summary>
    [StringLength(50)]
    public string? TimeZone { get; init; }

    /// <summary>
    /// Gets whether to create a personal organization during registration.
    /// Defaults to false.
    /// </summary>
    public bool CreateOrganization { get; init; } = false;
}
