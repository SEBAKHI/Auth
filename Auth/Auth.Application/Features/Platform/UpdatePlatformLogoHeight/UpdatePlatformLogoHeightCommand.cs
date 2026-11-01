using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.UpdatePlatformLogoHeight;

/// <summary>
/// Command to set the height the platform's logo is shown at on the sign-in pages. The image
/// is not touched, and neither is the rest of the branding: it is saved on its own, as the
/// appearance is (<c>UpdatePlatformThemeCommand</c>), so neither can put back a stale copy of
/// the other.
/// </summary>
/// <param name="LogoHeightPx">The height in pixels, or null to go back to the default.</param>
/// <param name="UpdatedBy">ID of the admin performing the update.</param>
public record UpdatePlatformLogoHeightCommand(
    int? LogoHeightPx,
    Guid UpdatedBy) : IRequest<ErrorOr<PlatformSettingsDto>>;
