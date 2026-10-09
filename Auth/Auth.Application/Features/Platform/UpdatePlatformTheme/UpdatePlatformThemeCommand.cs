using Auth.Application.DTOs;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.UpdatePlatformTheme;

/// <summary>
/// Command to replace the platform appearance (base colour, theme, chart
/// colour, radius and menu accent). The branding — name, logos, favicon — is
/// saved by <c>UpdatePlatformSettingsCommand</c> and is not touched here.
/// </summary>
/// <param name="BaseColor">The base colour choice.</param>
/// <param name="Theme">The theme choice.</param>
/// <param name="Chart">The chart colour choice.</param>
/// <param name="Radius">The radius step.</param>
/// <param name="MenuAccent">The menu accent.</param>
/// <param name="UpdatedBy">ID of the admin performing the update.</param>
public record UpdatePlatformThemeCommand(
    ThemeColorChoice? BaseColor,
    ThemeColorChoice? Theme,
    ThemeColorChoice? Chart,
    string? Radius,
    string? MenuAccent,
    Guid UpdatedBy) : IRequest<ErrorOr<PlatformSettingsDto>>;
