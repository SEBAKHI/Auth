using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// The height a logo is shown at on the sign-in pages, in CSS pixels: the area the logo takes
/// on the page, never the image file, which is not re-encoded or reframed by it. The width
/// follows from the image's own proportions.
/// </summary>
/// <remarks>
/// Its owner holds it as optional: none chosen means the owner's default
/// (<see cref="LogoHeights.PlatformDefaultPx"/>, <see cref="LogoHeights.ApplicationDefaultPx"/>),
/// so a later change of the default reaches every owner that never chose.
/// </remarks>
public sealed record class LogoHeight
{
    /// <summary>Gets the height in pixels, from <see cref="LogoHeights.MinPx"/> to <see cref="LogoHeights.MaxPx"/>.</summary>
    public int Px { get; }

    private LogoHeight(int px) => Px = px;

    /// <summary>
    /// The height an administrator chose: null means none (the default applies).
    /// <c>LogoHeight.OutOfRange</c> outside <see cref="LogoHeights.MinPx"/>–<see cref="LogoHeights.MaxPx"/>.
    /// </summary>
    public static ErrorOr<LogoHeight?> CreateOptional(int? px)
    {
        if (px is null)
        {
            return (LogoHeight?)null;
        }

        return px is >= LogoHeights.MinPx and <= LogoHeights.MaxPx
            ? new LogoHeight(px.Value)
            : LogoHeightErrors.OutOfRange(LogoHeights.MinPx, LogoHeights.MaxPx);
    }

    /// <summary>
    /// Rebuilds a stored height. The column holds the same range under a CHECK; a value outside
    /// it reads as none chosen, so a page never draws a logo at a size the rules refuse.
    /// </summary>
    public static LogoHeight? FromStored(int? px) =>
        px is >= LogoHeights.MinPx and <= LogoHeights.MaxPx ? new LogoHeight(px.Value) : null;
}
