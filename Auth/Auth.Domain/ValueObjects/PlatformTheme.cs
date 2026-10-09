using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// One of the three colour choices of the platform appearance: a name from
/// <see cref="ThemePresets"/>, or <see cref="ThemePresets.Custom"/> with one
/// <c>#rrggbb</c> colour per mode.
/// </summary>
/// <param name="Preset">A registry name, or <see cref="ThemePresets.Custom"/>.</param>
/// <param name="Light">The light-mode colour; set exactly when the preset is custom.</param>
/// <param name="Dark">The dark-mode colour; set exactly when the preset is custom.</param>
public sealed record ThemeColorChoice(string Preset, string? Light = null, string? Dark = null)
{
    /// <summary>Gets whether the administrator picked the colours themselves.</summary>
    public bool IsCustom => Preset == ThemePresets.Custom;
}

/// <summary>
/// The platform's appearance: the selections of shadcn/ui's theme builder
/// (https://ui.shadcn.com/create) that both front-end apps render for every
/// visitor, including the anonymous sign-in pages.
/// </summary>
/// <remarks>
/// Only names and <c>#rrggbb</c> colours are ever accepted, so a stored value
/// can select a palette but never write CSS: the console derives every colour
/// value from its own copy of the registry.
/// </remarks>
public sealed partial record PlatformTheme
{
    /// <summary>Slot names, used in error arguments and the stored form.</summary>
    public const string BaseSlot = "base";
    public const string ThemeSlot = "theme";
    public const string ChartSlot = "chart";

    /// <summary>The appearance of an installation nobody has customised.</summary>
    public static readonly PlatformTheme Default = new(
        new ThemeColorChoice(ThemePresets.DefaultBaseColor),
        new ThemeColorChoice(ThemePresets.DefaultTheme),
        new ThemeColorChoice(ThemePresets.DefaultChartColor),
        ThemePresets.DefaultRadius,
        ThemePresets.DefaultMenuAccent);

    private PlatformTheme(
        ThemeColorChoice baseColor,
        ThemeColorChoice theme,
        ThemeColorChoice chart,
        string radius,
        string menuAccent)
    {
        Base = baseColor;
        Theme = theme;
        Chart = chart;
        Radius = radius;
        MenuAccent = menuAccent;
    }

    /// <summary>Gets the base colour: backgrounds, cards, borders and muted text.</summary>
    public ThemeColorChoice Base { get; }

    /// <summary>Gets the theme: primary buttons, links and the active sidebar item.</summary>
    public ThemeColorChoice Theme { get; }

    /// <summary>Gets the chart colour: the five series colours of every chart.</summary>
    public ThemeColorChoice Chart { get; }

    /// <summary>Gets the corner radius step.</summary>
    public string Radius { get; }

    /// <summary>Gets how a highlighted menu item is coloured.</summary>
    public string MenuAccent { get; }

    /// <summary>
    /// Validates an appearance and returns it in canonical form: colours in
    /// lower case, and no colours kept on a choice that is not custom.
    /// </summary>
    public static ErrorOr<PlatformTheme> Create(
        ThemeColorChoice? baseColor,
        ThemeColorChoice? theme,
        ThemeColorChoice? chart,
        string? radius,
        string? menuAccent)
    {
        var errors = new List<Error>();

        var normalizedBase = Normalize(BaseSlot, baseColor, ThemePresets.BaseColors.Contains, _ => true, errors);
        var baseName = normalizedBase?.Preset ?? string.Empty;
        bool AvailableForBase(string name) => ThemePresets.IsAvailableForBase(name, baseName);
        var normalizedTheme = Normalize(ThemeSlot, theme, IsRegistryName, AvailableForBase, errors);
        var normalizedChart = Normalize(ChartSlot, chart, IsRegistryName, AvailableForBase, errors);

        if (radius is null || !ThemePresets.Radii.Contains(radius))
        {
            errors.Add(SystemSettingsErrors.ThemeUnknownRadius(radius ?? string.Empty));
        }

        if (menuAccent is null || !ThemePresets.MenuAccents.Contains(menuAccent))
        {
            errors.Add(SystemSettingsErrors.ThemeUnknownMenuAccent(menuAccent ?? string.Empty));
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        return new PlatformTheme(normalizedBase!, normalizedTheme!, normalizedChart!, radius!, menuAccent!);
    }

    private static ThemeColorChoice? Normalize(
        string slot,
        ThemeColorChoice? choice,
        Func<string, bool> isKnownPreset,
        Func<string, bool> isAllowedPreset,
        List<Error> errors)
    {
        if (choice is null || string.IsNullOrEmpty(choice.Preset))
        {
            errors.Add(SystemSettingsErrors.ThemeChoiceRequired(slot));
            return null;
        }

        if (choice.IsCustom)
        {
            if (!IsHexColor(choice.Light) || !IsHexColor(choice.Dark))
            {
                errors.Add(SystemSettingsErrors.ThemeCustomColorInvalid(slot));
                return null;
            }

            return new ThemeColorChoice(
                ThemePresets.Custom,
                choice.Light!.ToLowerInvariant(),
                choice.Dark!.ToLowerInvariant());
        }

        if (!isKnownPreset(choice.Preset))
        {
            errors.Add(SystemSettingsErrors.ThemeUnknownColor(slot, choice.Preset));
            return null;
        }

        if (!isAllowedPreset(choice.Preset))
        {
            // A monochrome theme of a base colour other than the one chosen
            // (or of any base, when the base is custom).
            errors.Add(SystemSettingsErrors.ThemeColorUnavailableForBase(slot, choice.Preset));
            return null;
        }

        // Colours sent alongside a registry name are leftovers of a custom
        // choice the administrator switched away from; they mean nothing.
        return new ThemeColorChoice(choice.Preset);
    }

    private static bool IsRegistryName(string name) =>
        ThemePresets.BaseColors.Contains(name) || ThemePresets.AccentColors.Contains(name);

    private static bool IsHexColor(string? value) => value is not null && HexColor().IsMatch(value);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
