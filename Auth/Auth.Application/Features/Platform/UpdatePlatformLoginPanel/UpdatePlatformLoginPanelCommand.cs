using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.UpdatePlatformLoginPanel;

/// <summary>
/// Command to show or hide the panel beside the sign-in form. Saved on its own, as the logo
/// height is, so a stale copy of the rest of the settings cannot put back an older choice.
/// </summary>
/// <param name="Shown">Whether the sign-in pages have the panel.</param>
/// <param name="UpdatedBy">ID of the admin performing the update.</param>
public record UpdatePlatformLoginPanelCommand(
    bool Shown,
    Guid UpdatedBy) : IRequest<ErrorOr<PlatformSettingsDto>>;
