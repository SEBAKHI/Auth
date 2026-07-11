namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for the full platform settings (admin view).
/// </summary>
public class PlatformSettingsDto
{
    public string PlatformName { get; set; } = string.Empty;

    /// <summary>
    /// Public URL of the uploaded logo, or null when no logo is set.
    /// </summary>
    public string? LogoUrl { get; set; }

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
}
