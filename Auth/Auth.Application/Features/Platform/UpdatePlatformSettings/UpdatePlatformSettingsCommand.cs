using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.UpdatePlatformSettings;

/// <summary>
/// Command to update the platform branding settings.
/// </summary>
/// <param name="PlatformName">New display name of the platform.</param>
/// <param name="LogoUrl">Uploaded logo image key, or null to clear the logo.</param>
/// <param name="UpdatedBy">ID of the admin performing the update.</param>
public record UpdatePlatformSettingsCommand(
    string PlatformName,
    string? LogoUrl,
    Guid UpdatedBy) : IRequest<ErrorOr<PlatformSettingsDto>>;
