namespace Auth.Domain.Constants;

/// <summary>
/// The closed vocabulary of the platform appearance settings: the names of
/// shadcn/ui's colour registry (the Base Color, Theme and Chart Color pickers of
/// https://ui.shadcn.com/create), its radius steps and its menu accents.
/// </summary>
/// <remarks>
/// The API stores names, never colour values: the console builds the CSS from
/// its verbatim copy of the registry
/// (<c>Auth_UI/packages/ui/src/theme/shadcn-themes.ts</c>), so a stored value can
/// only ever select an entry, not inject one. <c>build-theme.test.ts</c> reads
/// this file and fails when the two lists differ.
/// </remarks>
public static class ThemePresets
{
    /// <summary>
    /// The value that replaces a registry name when the administrator picks a
    /// colour per mode instead.
    /// </summary>
    public const string Custom = "custom";

    /// <summary>Base colours, in shadcn's order.</summary>
    public static readonly IReadOnlyList<string> BaseColors =
    [
        "neutral",
        "stone",
        "zinc",
        "mauve",
        "olive",
        "mist",
        "taupe",
    ];

    /// <summary>
    /// The colourful themes, in shadcn's order. Combined with any base colour,
    /// and also the chart colours.
    /// </summary>
    public static readonly IReadOnlyList<string> AccentColors =
    [
        "amber",
        "blue",
        "cyan",
        "emerald",
        "fuchsia",
        "green",
        "indigo",
        "lime",
        "orange",
        "pink",
        "purple",
        "red",
        "rose",
        "sky",
        "teal",
        "violet",
        "yellow",
    ];

    /// <summary>Corner radius steps.</summary>
    public static readonly IReadOnlyList<string> Radii =
    [
        "default",
        "none",
        "small",
        "medium",
        "large",
    ];

    /// <summary>How a highlighted menu item is coloured.</summary>
    public static readonly IReadOnlyList<string> MenuAccents =
    [
        "subtle",
        "bold",
    ];

    /// <summary>The base colour of an installation nobody has customised.</summary>
    public const string DefaultBaseColor = "neutral";

    /// <summary>The theme of an installation nobody has customised.</summary>
    public const string DefaultTheme = "neutral";

    /// <summary>
    /// The chart colour of an installation nobody has customised: the shipped
    /// preset draws its charts in Cyan.
    /// </summary>
    public const string DefaultChartColor = "cyan";

    /// <summary>The radius of an installation nobody has customised.</summary>
    public const string DefaultRadius = "default";

    /// <summary>The menu accent of an installation nobody has customised.</summary>
    public const string DefaultMenuAccent = "subtle";

    /// <summary>
    /// Whether a registry theme or chart colour may be combined with a base
    /// colour: the base itself (a monochrome theme) or any colourful theme —
    /// shadcn's <c>getThemesForBaseColor</c>. A custom base has no registry
    /// entry to be monochrome with, so only the colourful themes remain.
    /// </summary>
    public static bool IsAvailableForBase(string name, string baseColor) =>
        (name == baseColor && BaseColors.Contains(baseColor)) || AccentColors.Contains(name);
}
