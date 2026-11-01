namespace Auth.Domain.Constants;

/// <summary>
/// The heights, in CSS pixels, a logo may be given on the sign-in pages: the area the logo
/// takes on the page, never the image file. The console's copy (<c>LOGO_HEIGHT</c> in
/// <c>packages/ui/src/logo-mark.tsx</c>) is held to these values by a test that reads this file.
/// </summary>
public static class LogoHeights
{
    /// <summary>The lowest height an administrator may choose.</summary>
    public const int MinPx = 24;

    /// <summary>The tallest height an administrator may choose.</summary>
    public const int MaxPx = 120;

    /// <summary>The platform logo's height while none is chosen: 1.5 × the 40px it had before.</summary>
    public const int PlatformDefaultPx = 60;

    /// <summary>An application logo's height while none is chosen: the 48px it always had.</summary>
    public const int ApplicationDefaultPx = 48;
}
