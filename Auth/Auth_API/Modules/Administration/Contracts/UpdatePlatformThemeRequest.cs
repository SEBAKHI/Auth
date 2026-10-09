namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request to replace the platform appearance: the selections of shadcn/ui's
/// theme builder (https://ui.shadcn.com/create).
/// </summary>
/// <param name="Base">Base colour: neutral, stone, zinc, mauve, olive, mist, taupe, or custom.</param>
/// <param name="Theme">Theme: the chosen base colour itself, a colourful theme (amber … yellow), or custom.</param>
/// <param name="Chart">Chart colour: the same choices as the theme.</param>
/// <param name="Radius">Corner radius: default, none, small, medium or large.</param>
/// <param name="MenuAccent">Highlighted menu item colour: subtle or bold.</param>
public record UpdatePlatformThemeRequest(
    ThemeColorChoiceRequest? Base,
    ThemeColorChoiceRequest? Theme,
    ThemeColorChoiceRequest? Chart,
    string? Radius,
    string? MenuAccent);

/// <summary>
/// One colour choice of the platform appearance.
/// </summary>
/// <param name="Preset">A registry name, or "custom".</param>
/// <param name="Light">For "custom": the light-mode colour as #rrggbb. Ignored otherwise.</param>
/// <param name="Dark">For "custom": the dark-mode colour as #rrggbb. Ignored otherwise.</param>
public record ThemeColorChoiceRequest(string? Preset, string? Light = null, string? Dark = null);
