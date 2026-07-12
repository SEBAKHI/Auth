namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request to update the platform branding settings.
/// </summary>
/// <param name="PlatformName">New display name of the platform.</param>
/// <param name="LogoUrl">Uploaded light-mode logo image key, or null to clear the logo.</param>
/// <param name="LogoUrlDark">Uploaded dark-mode logo image key, or null to clear the dark-mode logo.</param>
/// <param name="FaviconUrl">Uploaded favicon image key, or null to clear the favicon.</param>
public record UpdatePlatformSettingsRequest(
    string PlatformName,
    string? LogoUrl,
    string? LogoUrlDark,
    string? FaviconUrl);
