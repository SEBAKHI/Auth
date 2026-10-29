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

    /// <summary>
    /// Whether the sign-in page offers the "Stay signed in" box: true only while
    /// the person decides (<c>IdentityProvider:StaySignedInMode</c> =
    /// <c>PersonChooses</c>). Only this boolean is published, never the mode: the
    /// page acts on nothing else, and the server applies the setting to every
    /// sign-in whatever the page sends (OI-165).
    /// </summary>
    public bool StaySignedInOffered { get; set; } = true;

    /// <summary>
    /// Whether the sign-in page offers a code sent by email (OI-215): true while email
    /// sending is on. A hint for the page, which the browser caches with the rest of
    /// this answer: the server decides every request on its own settings.
    /// </summary>
    public bool CodeSignInOffered { get; set; } = true;

    /// <summary>
    /// Which method the sign-in page puts first (OI-215, D-215-6 (a)): <c>code</c> when a
    /// new account signs up without a password, otherwise <c>password</c>. The other
    /// method stays a visible link.
    /// </summary>
    public string PrimaryMethod { get; set; } = SignInMethods.Password;

    /// <summary>Whether a visitor may create an account (<c>Registration:AllowSelfRegistration</c>).</summary>
    public bool SelfRegistrationOpen { get; set; } = true;

    /// <summary>
    /// Whether a sign-up asks for a password (OI-215): the switch, honoured only while
    /// email is on.
    /// </summary>
    public bool PasswordAtSignUp { get; set; } = true;

    /// <summary>Whether a sign-up asks for a first and last name (OI-215).</summary>
    public bool NameAtSignUp { get; set; } = true;

    /// <summary>
    /// The design the platform's own sign-in pages show in their panel, or null for the plain
    /// panel. The page fetches the rendered design only when this is present, and keys its copy
    /// on <c>ContentHash</c>.
    /// </summary>
    public LoginDesignRefDto? LoginDesign { get; set; }
}

/// <summary>
/// The wire names of the two sign-in methods the sign-in page offers (OI-215).
/// </summary>
public static class SignInMethods
{
    /// <summary>The address and the password.</summary>
    public const string Password = "password";

    /// <summary>A code emailed to the address.</summary>
    public const string Code = "code";
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

    /// <summary>The height of every control (buttons, inputs, selects, toggles…).</summary>
    public ThemeSizeChoiceDto ControlSize { get; set; } = ThemeSizeChoiceDto.From(ThemeSizeChoice.Default);

    /// <summary>The height of a table row holding one line of text.</summary>
    public ThemeSizeChoiceDto TableRowHeight { get; set; } = ThemeSizeChoiceDto.From(ThemeSizeChoice.Default);

    public static PlatformThemeDto From(PlatformTheme theme) => new()
    {
        Base = ThemeColorChoiceDto.From(theme.Base),
        Theme = ThemeColorChoiceDto.From(theme.Theme),
        Chart = ThemeColorChoiceDto.From(theme.Chart),
        Radius = theme.Radius,
        MenuAccent = theme.MenuAccent,
        ControlSize = ThemeSizeChoiceDto.From(theme.ControlSize),
        TableRowHeight = ThemeSizeChoiceDto.From(theme.TableRowHeight),
    };

    /// <summary>The DTO of an appearance that may be absent (an application's own).</summary>
    public static PlatformThemeDto? FromOrNull(PlatformTheme? theme) => theme is null ? null : From(theme);
}

/// <summary>
/// One size choice of the platform appearance.
/// </summary>
public class ThemeSizeChoiceDto
{
    /// <summary>A size step (<c>compact</c>, <c>default</c>, <c>comfortable</c>), or <c>"custom"</c>.</summary>
    public string Preset { get; set; } = ThemePresets.DefaultSize;

    /// <summary>The height in pixels of a custom choice; otherwise absent.</summary>
    public int? Px { get; set; }

    public static ThemeSizeChoiceDto From(ThemeSizeChoice choice) => new()
    {
        Preset = choice.Preset,
        Px = choice.Px,
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
