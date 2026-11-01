namespace Auth.Domain.Constants;

/// <summary>
/// The closed vocabulary of the platform appearance settings: the names of
/// shadcn/ui's colour registry (the Base Color, Theme and Chart Color pickers of
/// https://ui.shadcn.com/create), its radius steps and its menu accents,
/// the size steps of the control height and the table row height, and the
/// header icon sizes.
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

    /// <summary>
    /// The steps of the two size choices (control height and table row
    /// height), smallest first. <see cref="Custom"/> replaces a step when the
    /// administrator types a height in pixels instead.
    /// </summary>
    public static readonly IReadOnlyList<string> Sizes =
    [
        "compact",
        "default",
        "comfortable",
    ];

    /// <summary>
    /// The height of every control (buttons, inputs, selects, toggles…) at each
    /// of <see cref="Sizes"/>, in pixels. The middle one is the preset's own
    /// (Tailwind <c>h-9</c>).
    /// </summary>
    public static readonly IReadOnlyList<int> ControlHeightsPx =
    [
        32,
        36,
        40,
    ];

    /// <summary>The lowest custom control height, in pixels.</summary>
    public const int MinControlHeightPx = 28;

    /// <summary>The highest custom control height, in pixels.</summary>
    public const int MaxControlHeightPx = 56;

    /// <summary>
    /// The height of a table row holding one line of text at each of
    /// <see cref="Sizes"/>, in pixels. The middle one is the preset's own
    /// (<c>p-3</c> around a 20px line). Even the smallest step is no lower than
    /// the tallest control step, so every pair of steps is valid.
    /// </summary>
    public static readonly IReadOnlyList<int> TableRowHeightsPx =
    [
        40,
        44,
        48,
    ];

    /// <summary>The lowest custom table row height, in pixels.</summary>
    public const int MinTableRowHeightPx = 32;

    /// <summary>The highest custom table row height, in pixels.</summary>
    public const int MaxTableRowHeightPx = 72;

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
    /// The control height and the table row height of an installation nobody
    /// has customised, and of an appearance saved before either existed.
    /// </summary>
    public const string DefaultSize = "default";

    /// <summary>
    /// The sizes of the icon buttons and the avatar in the header of the console
    /// and the accounts app, smallest first. The header itself stays 56px tall at
    /// both. The avatar is as large as a button. The pixels belong to the front
    /// end (<c>Auth_UI/packages/ui/src/theme/theme-config.ts</c>):
    /// <list type="bullet">
    /// <item><c>small</c>: icon 16px, button and avatar 36px (the header's buttons before the setting existed).</item>
    /// <item><c>default</c>: icon 24px, button and avatar 40px (Google's header).</item>
    /// </list>
    /// No larger size (owner, 2026-11-02).
    /// </summary>
    public static readonly IReadOnlyList<string> HeaderIconSizes =
    [
        "small",
        "default",
    ];

    /// <summary>
    /// The header icon size of an installation nobody has customised, and of an
    /// appearance saved before the setting existed (owner decision, 2026-11-02).
    /// </summary>
    public const string DefaultHeaderIconSize = "default";

    /// <summary>
    /// The height in pixels of a size step on a scale (<see cref="ControlHeightsPx"/>
    /// or <see cref="TableRowHeightsPx"/>), or null for a name that is not a step.
    /// </summary>
    public static int? HeightOfStepPx(IReadOnlyList<int> scale, string step)
    {
        for (var index = 0; index < Sizes.Count; index++)
        {
            if (Sizes[index] == step)
            {
                return scale[index];
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a registry theme or chart colour may be combined with a base
    /// colour: the base itself (a monochrome theme) or any colourful theme —
    /// shadcn's <c>getThemesForBaseColor</c>. A custom base has no registry
    /// entry to be monochrome with, so only the colourful themes remain.
    /// </summary>
    public static bool IsAvailableForBase(string name, string baseColor) =>
        (name == baseColor && BaseColors.Contains(baseColor)) || AccentColors.Contains(name);
}
