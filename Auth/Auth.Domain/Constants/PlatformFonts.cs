namespace Auth.Domain.Constants;

/// <summary>
/// The limits on the platform's own font files (owner decision D7, 2026-10-31): WOFF2 only,
/// one file per role, each at most <see cref="MaxSizeBytes"/>. The console's copy
/// (<c>FONT_MAX_SIZE_BYTES</c> in <c>packages/ui/src/theme/platform-fonts.ts</c>) is held to
/// this value by a test that reads this file.
/// </summary>
public static class PlatformFonts
{
    /// <summary>The largest font file accepted: 2 MiB. Every page of both apps downloads it.</summary>
    public const long MaxSizeBytes = 2 * 1024 * 1024;

    /// <summary>The longest family name kept for display; a longer one is cut.</summary>
    public const int MaxFamilyNameLength = 100;
}
