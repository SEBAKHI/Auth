using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.UpdatePlatformHiddenLanguages;

/// <summary>
/// Command to set the languages hidden from the language menus of both apps. Nothing else in
/// the settings changes: it is saved on its own, as the appearance is
/// (<c>UpdatePlatformThemeCommand</c>), so neither can put back a stale copy of the other.
/// </summary>
/// <param name="HiddenLanguages">The language codes to hide; null or empty shows every language.</param>
/// <param name="UpdatedBy">ID of the admin performing the update.</param>
public record UpdatePlatformHiddenLanguagesCommand(
    IReadOnlyList<string?>? HiddenLanguages,
    Guid UpdatedBy) : IRequest<ErrorOr<PlatformSettingsDto>>;
