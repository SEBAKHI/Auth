namespace Auth_Lib.DTOs;

/// <summary>
/// DTO containing basic user information for responses.
/// </summary>
public record UserInfo
{
    /// <summary>
    /// Gets the user's unique identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the user's email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Gets the user's first name.
    /// </summary>
    public required string FirstName { get; init; }

    /// <summary>
    /// Gets the user's last name.
    /// </summary>
    public required string LastName { get; init; }

    /// <summary>
    /// Gets the user's display name.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the user's preferred language.
    /// </summary>
    public string? PreferredLanguage { get; init; }

    /// <summary>
    /// Gets the user's timezone.
    /// </summary>
    public string? TimeZone { get; init; }

    /// <summary>
    /// Gets the user's roles.
    /// </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>
    /// Gets the user's permissions.
    /// </summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];
}
