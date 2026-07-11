namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request to update the platform branding settings.
/// </summary>
/// <param name="PlatformName">New display name of the platform.</param>
/// <param name="LogoUrl">Uploaded logo image key, or null to clear the logo.</param>
public record UpdatePlatformSettingsRequest(
    string PlatformName,
    string? LogoUrl);
