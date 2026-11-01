namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request to set the height a logo is shown at on the sign-in pages: the area the logo takes
/// on the page, never the image file. The platform's logo and an application's take the same
/// request.
/// </summary>
/// <param name="LogoHeightPx">
/// The height in CSS pixels, a whole number from 24 to 120; null goes back to the default
/// (60 for the platform's logo, 48 for an application's). The width follows the image's own
/// proportions.
/// </param>
public record UpdateLogoHeightRequest(int? LogoHeightPx);
