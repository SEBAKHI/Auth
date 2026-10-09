using Auth.Domain.Constants;
using Auth.Domain.ValueObjects;

namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for the full platform settings (admin view).
/// </summary>
public class PlatformSettingsDto
{
    public string PlatformName { get; set; } = string.Empty;

    /// <summary>
    /// Public URL of the uploaded light-mode logo, or null when no logo is set.
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Public URL of the uploaded dark-mode logo, or null when no dark-mode
    /// logo is set (clients fall back to the light-mode logo).
    /// </summary>
    public string? LogoUrlDark { get; set; }

    /// <summary>
    /// Public URL of the uploaded favicon, or null when no favicon is set
    /// (clients fall back to the theme logo, then the default icon).
    /// </summary>
    public string? FaviconUrl { get; set; }

    /// <summary>
    /// The platform appearance: base colour, theme, chart colour, radius and
    /// menu accent. Always present; an installation nobody has customised
    /// returns the shipped preset.
    /// </summary>
    public PlatformThemeDto Theme { get; set; } = PlatformThemeDto.From(PlatformTheme.Default);

    public DateTime? ModifiedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public string? ModifiedByName { get; set; }
}

/// <summary>
/// Minimal branding payload served anonymously to render the platform
/// name/logo on pre-auth screens (login, invitations) and the browser tab.
/// </summary>
public class PlatformBrandingDto
{
    public string PlatformName { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? LogoUrlDark { get; set; }
    public string? FaviconUrl { get; set; }

    /// <summary>
    /// The platform appearance, rendered by both apps for every visitor.
    /// </summary>
    public PlatformThemeDto Theme { get; set; } = PlatformThemeDto.From(PlatformTheme.Default);
}

/// <summary>
/// The platform appearance as shadcn/ui's theme builder describes it. Values
/// are registry names (<see cref="ThemePresets"/>); a colour choice of
/// <c>"custom"</c> carries a <c>#rrggbb</c> colour per mode instead.
/// </summary>
public class PlatformThemeDto
{
    public ThemeColorChoiceDto Base { get; set; } = new();
    public ThemeColorChoiceDto Theme { get; set; } = new();
    public ThemeColorChoiceDto Chart { get; set; } = new();
    public string Radius { get; set; } = ThemePresets.DefaultRadius;
    public string MenuAccent { get; set; } = ThemePresets.DefaultMenuAccent;

    public static PlatformThemeDto From(PlatformTheme theme) => new()
    {
        Base = ThemeColorChoiceDto.From(theme.Base),
        Theme = ThemeColorChoiceDto.From(theme.Theme),
        Chart = ThemeColorChoiceDto.From(theme.Chart),
        Radius = theme.Radius,
        MenuAccent = theme.MenuAccent,
    };
}

/// <summary>
/// One colour choice of the platform appearance.
/// </summary>
public class ThemeColorChoiceDto
{
    /// <summary>A registry name, or <c>"custom"</c>.</summary>
    public string Preset { get; set; } = string.Empty;

    /// <summary>The light-mode <c>#rrggbb</c> colour of a custom choice; otherwise absent.</summary>
    public string? Light { get; set; }

    /// <summary>The dark-mode <c>#rrggbb</c> colour of a custom choice; otherwise absent.</summary>
    public string? Dark { get; set; }

    public static ThemeColorChoiceDto From(ThemeColorChoice choice) => new()
    {
        Preset = choice.Preset,
        Light = choice.Light,
        Dark = choice.Dark,
    };
}
