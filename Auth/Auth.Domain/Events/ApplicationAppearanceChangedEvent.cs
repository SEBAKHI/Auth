using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an application's own appearance changed: switched on or off, or replaced. While
/// it is on it replaces the platform's appearance on the accounts pages the application sends
/// people to, so a change is recorded with what it was before and after, and by whom.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="ApplicationId">The application whose appearance changed.</param>
/// <param name="ApplicationCode">Its code (<c>client_id</c>).</param>
/// <param name="Before">The appearance before the save.</param>
/// <param name="After">The appearance after the save.</param>
/// <param name="TriggeredBy">The administrator who saved it.</param>
/// <param name="OccurredAt">When the change was stored.</param>
public sealed record ApplicationAppearanceChangedEvent(
    Guid EventId,
    Guid ApplicationId,
    string ApplicationCode,
    ApplicationAppearanceSnapshot Before,
    ApplicationAppearanceSnapshot After,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>An application's appearance at one moment: the switch and the saved choices.</summary>
/// <param name="Enabled">Whether it replaced the platform's appearance.</param>
/// <param name="Theme">The saved choices; null when none was saved.</param>
public sealed record ApplicationAppearanceSnapshot(bool Enabled, ThemeSnapshot? Theme)
{
    /// <summary>The snapshot of an application's switch and appearance.</summary>
    public static ApplicationAppearanceSnapshot Of(bool enabled, PlatformTheme? theme) =>
        new(enabled, theme is null ? null : ThemeSnapshot.Of(theme));
}

/// <summary>A <see cref="PlatformTheme"/> as plain values, the form an event carries.</summary>
public sealed record ThemeSnapshot(
    ThemeColorSnapshot Base,
    ThemeColorSnapshot Theme,
    ThemeColorSnapshot Chart,
    string Radius,
    string MenuAccent,
    ThemeSizeSnapshot ControlSize,
    ThemeSizeSnapshot TableRowHeight)
{
    /// <summary>The snapshot of <paramref name="theme"/>.</summary>
    public static ThemeSnapshot Of(PlatformTheme theme) => new(
        ThemeColorSnapshot.Of(theme.Base),
        ThemeColorSnapshot.Of(theme.Theme),
        ThemeColorSnapshot.Of(theme.Chart),
        theme.Radius,
        theme.MenuAccent,
        ThemeSizeSnapshot.Of(theme.ControlSize),
        ThemeSizeSnapshot.Of(theme.TableRowHeight));
}

/// <summary>One colour choice: a registry name, or <c>custom</c> with a colour per mode.</summary>
public sealed record ThemeColorSnapshot(string Preset, string? Light, string? Dark)
{
    /// <summary>The snapshot of <paramref name="choice"/>.</summary>
    public static ThemeColorSnapshot Of(ThemeColorChoice choice) => new(choice.Preset, choice.Light, choice.Dark);
}

/// <summary>One size choice: a step, or <c>custom</c> with a height in pixels.</summary>
public sealed record ThemeSizeSnapshot(string Preset, int? Px)
{
    /// <summary>The snapshot of <paramref name="choice"/>.</summary>
    public static ThemeSizeSnapshot Of(ThemeSizeChoice choice) => new(choice.Preset, choice.Px);
}
