using Auth_API.Modules.Administration.Contracts;

namespace Auth_API.Modules.ApplicationManagement.Contracts;

/// <summary>
/// Request to switch an application's own appearance on or off, and to save it. While it is on,
/// the accounts pages the application sends people to show it in place of the platform's
/// appearance, as a whole.
/// </summary>
/// <param name="Enabled">Whether the application's appearance replaces the platform's.</param>
/// <param name="Theme">
/// The appearance, with the same choices and rules as the platform's. Required when
/// <paramref name="Enabled"/> is true; when false it may be left out, and the saved one is kept.
/// </param>
public record UpdateApplicationAppearanceRequest(bool Enabled, UpdatePlatformThemeRequest? Theme);
