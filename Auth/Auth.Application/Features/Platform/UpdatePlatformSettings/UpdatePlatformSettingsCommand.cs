using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.UpdatePlatformSettings;

/// <summary>
/// Command to update the platform branding settings.
/// </summary>
/// <param name="PlatformName">New display name of the platform.</param>
/// <param name="LogoUrl">Uploaded light-mode logo image key, or null to clear the logo.</param>
/// <param name="LogoUrlDark">Uploaded dark-mode logo image key, or null to clear the dark-mode logo.</param>
/// <param name="FaviconUrl">Uploaded favicon image key, or null to clear the favicon.</param>
/// <param name="UpdatedBy">ID of the admin performing the update.</param>
public record UpdatePlatformSettingsCommand(
    string PlatformName,
    string? LogoUrl,
    string? LogoUrlDark,
    string? FaviconUrl,
    Guid UpdatedBy) : IRequest<ErrorOr<PlatformSettingsDto>>;
