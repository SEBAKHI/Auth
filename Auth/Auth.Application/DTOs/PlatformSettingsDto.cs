namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for the full platform settings (admin view).
/// </summary>
public class PlatformSettingsDto
{
    public string PlatformName { get; set; } = string.Empty;

    /// <summary>
    /// Public URL of the uploaded light-mode logo, or null when no logo is set.
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Public URL of the uploaded dark-mode logo, or null when no dark-mode
    /// logo is set (clients fall back to the light-mode logo).
    /// </summary>
    public string? LogoUrlDark { get; set; }

    /// <summary>
    /// Public URL of the uploaded favicon, or null when no favicon is set
    /// (clients fall back to the theme logo, then the default icon).
    /// </summary>
    public string? FaviconUrl { get; set; }

    public DateTime? ModifiedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public string? ModifiedByName { get; set; }
}

/// <summary>
/// Minimal branding payload served anonymously to render the platform
/// name/logo on pre-auth screens (login, invitations) and the browser tab.
/// </summary>
public class PlatformBrandingDto
{
    public string PlatformName { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? LogoUrlDark { get; set; }
    public string? FaviconUrl { get; set; }
}
