using Auth.Application.DTOs;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.UpdateApplicationAppearance;

/// <summary>
/// Switches an application's own appearance on or off, and replaces it when one is sent. While
/// it is on, the accounts pages the application sends people to show it in place of the
/// platform's appearance; switching it off keeps it saved.
/// </summary>
/// <remarks>
/// Not a field on the update contract, for the reason given at <c>SetApplicationActiveCommand</c>:
/// the console's update is a full-object PUT built from possibly stale client state, and a logo
/// upload must not put back an appearance an administrator has just switched off.
/// </remarks>
/// <param name="ApplicationId">The application.</param>
/// <param name="Enabled">Whether the application's appearance replaces the platform's.</param>
/// <param name="Theme">The appearance to save; required to switch it on, and null keeps the one saved.</param>
public sealed record UpdateApplicationAppearanceCommand(
    Guid ApplicationId,
    bool Enabled,
    ThemeChoices? Theme) : IRequest<ErrorOr<ApplicationAppearanceDto>>
{
    /// <summary>The administrator saving it.</summary>
    public Guid ChangedBy { get; init; }
}

/// <summary>
/// The choices of an appearance as a request carries them, unvalidated: the same as the
/// platform's (<c>UpdatePlatformThemeCommand</c>).
/// </summary>
public sealed record ThemeChoices(
    ThemeColorChoice? BaseColor,
    ThemeColorChoice? Theme,
    ThemeColorChoice? Chart,
    string? Radius,
    string? MenuAccent,
    ThemeSizeChoice? ControlSize,
    ThemeSizeChoice? TableRowHeight)
{
    /// <summary>The appearance these choices make, under the platform's rules.</summary>
    public ErrorOr<PlatformTheme> ToTheme() =>
        PlatformTheme.Create(BaseColor, Theme, Chart, Radius, MenuAccent, ControlSize, TableRowHeight);
}
