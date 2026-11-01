namespace Auth.Application.DTOs;

/// <summary>
/// One application the calling user can open from IAM's Home: a tile with its logo, its name
/// and the address that enters it (UI batch item 12).
/// </summary>
/// <remarks>
/// Listed only when the sign-in gate admits the user and the registration offers a way in, so
/// <see cref="LaunchUrl"/> is never null. Nothing about how the user got access is sent: the
/// tile does not show it, and the user cannot act on it.
/// </remarks>
public class MyApplicationDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Public URL of the logo, or null when the application has none.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Public URL of the dark-mode logo, or null when the application has one logo for both
    /// modes (clients fall back to the light-mode logo).
    /// </summary>
    public string? LogoUrlDark { get; set; }

    /// <summary>
    /// Where the tile opens the application: its initiate-login URI with <c>iss</c>, else its
    /// home URL, from <c>ApplicationLaunchTarget.ForHomeTile</c>. Absolute, <c>https</c>, and
    /// without user information.
    /// </summary>
    public string LaunchUrl { get; set; } = string.Empty;
}
