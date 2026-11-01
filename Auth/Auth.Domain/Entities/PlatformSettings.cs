using Auth.Domain.Enums;
using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Entities;

/// <summary>
/// Platform-wide branding settings (single-row aggregate).
/// The console UI reads these to render the platform name and logo on the
/// sidebar, auth screens, and the browser tab.
/// </summary>
public class PlatformSettings : EntityBase
{
    /// <summary>
    /// Fixed identifier of the single settings row; enforced by a CHECK
    /// constraint in the database.
    /// </summary>
    public static readonly Guid SingletonId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Platform name used when no settings row has been customized yet.
    /// </summary>
    public const string DefaultPlatformName = "Auth Console";

    /// <summary>
    /// Gets the display name of the platform.
    /// </summary>
    public string PlatformName { get; private set; } = DefaultPlatformName;

    /// <summary>
    /// Gets the uploaded light-mode logo image key (relative key composed into
    /// a public URL at the API edge), or null when no logo has been uploaded.
    /// </summary>
    public string? LogoUrl { get; private set; }

    /// <summary>
    /// Gets the uploaded dark-mode logo image key, or null when no dark-mode
    /// logo has been uploaded (clients fall back to the light-mode logo).
    /// </summary>
    public string? LogoUrlDark { get; private set; }

    /// <summary>
    /// Gets the uploaded favicon image key, or null when no favicon has been
    /// uploaded (clients fall back to the theme logo, then the default icon).
    /// A dedicated square mark: wordmark logos are illegible at tab-icon size.
    /// </summary>
    public string? FaviconUrl { get; private set; }

    /// <summary>
    /// Gets the platform's appearance: base colour, theme, chart colour,
    /// radius and menu accent, as chosen in shadcn's theme builder.
    /// </summary>
    public PlatformTheme Theme { get; private set; } = PlatformTheme.Default;

    /// <summary>
    /// Gets the height the logo is shown at on the sign-in pages, or null while none is chosen
    /// (<see cref="Constants.LogoHeights.PlatformDefaultPx"/> applies). Light and dark logos share it.
    /// </summary>
    public LogoHeight? LogoHeight { get; private set; }

    private readonly Dictionary<FontRole, PlatformFont> _fonts = [];

    /// <summary>
    /// Gets the platform's own fonts by role; a role with none uses the shipped IBM Plex
    /// Sans (a heading without its own font uses the body's).
    /// </summary>
    public IReadOnlyDictionary<FontRole, PlatformFont> Fonts => _fonts.AsReadOnly();

    /// <summary>
    /// Gets the languages hidden from the language menus of both apps; never English.
    /// </summary>
    public HiddenLanguages HiddenLanguages { get; private set; } = HiddenLanguages.None;

    /// <summary>
    /// Gets the last modification timestamp.
    /// </summary>
    public DateTime? ModifiedAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who last modified the settings.
    /// </summary>
    public Guid? ModifiedBy { get; private set; }

    private PlatformSettings() : base(SingletonId)
    {
    }

    public PlatformSettings(
        Guid id,
        string platformName,
        string? logoUrl,
        string? logoUrlDark,
        string? faviconUrl,
        DateTime? modifiedAt,
        Guid? modifiedBy,
        PlatformTheme? theme = null,
        LogoHeight? logoHeight = null,
        IReadOnlyDictionary<FontRole, PlatformFont>? fonts = null,
        HiddenLanguages? hiddenLanguages = null) : base(id)
    {
        HiddenLanguages = hiddenLanguages ?? HiddenLanguages.None;
        PlatformName = platformName;
        LogoUrl = logoUrl;
        LogoUrlDark = logoUrlDark;
        FaviconUrl = faviconUrl;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
        Theme = theme ?? PlatformTheme.Default;
        LogoHeight = logoHeight;
        foreach (var (role, font) in fonts ?? new Dictionary<FontRole, PlatformFont>())
        {
            _fonts[role] = font;
        }
    }

    /// <summary>
    /// Creates the default settings used before any customization is stored.
    /// </summary>
    public static PlatformSettings CreateDefault() => new();

    /// <summary>
    /// Applies new branding values and stamps the modification audit fields.
    /// </summary>
    public void Update(string platformName, string? logoUrl, string? logoUrlDark, string? faviconUrl, Guid modifiedBy)
    {
        PlatformName = platformName;
        LogoUrl = logoUrl;
        LogoUrlDark = logoUrlDark;
        FaviconUrl = faviconUrl;
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
    }

    /// <summary>
    /// Replaces the appearance and stamps the modification audit fields. The
    /// branding (name, logos, favicon) is untouched: the console saves the two
    /// independently, so neither can overwrite the other with a stale copy.
    /// </summary>
    public void UpdateTheme(PlatformTheme theme, Guid modifiedBy)
    {
        Theme = theme;
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
    }

    /// <summary>
    /// Sets the logo's height on the sign-in pages (null: back to the default) and stamps the
    /// modification audit fields. Nothing else changes: it is saved on its own, like the
    /// appearance, so a stale copy of the branding cannot put back an older height.
    /// </summary>
    /// <returns>False, and no change, when the height was already this.</returns>
    public bool SetLogoHeight(LogoHeight? logoHeight, Guid modifiedBy)
    {
        if (Equals(LogoHeight, logoHeight))
        {
            return false;
        }

        LogoHeight = logoHeight;
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
        return true;
    }

    /// <summary>
    /// Sets the languages hidden from the language menus and stamps the modification audit
    /// fields. Nothing else changes: it is saved on its own, like the appearance.
    /// </summary>
    /// <returns>False, and no change, when these languages were already the hidden ones.</returns>
    public bool SetHiddenLanguages(HiddenLanguages hiddenLanguages, Guid modifiedBy)
    {
        if (HiddenLanguages.Equals(hiddenLanguages))
        {
            return false;
        }

        HiddenLanguages = hiddenLanguages;
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
        return true;
    }

    /// <summary>The font of <paramref name="role"/>, or null while it uses the shipped one.</summary>
    public PlatformFont? FontFor(FontRole role) => _fonts.GetValueOrDefault(role);

    /// <summary>
    /// Sets the font of one role (null: back to the shipped one) and stamps the modification
    /// audit fields. Nothing else changes: fonts are saved on their own, like the appearance.
    /// </summary>
    /// <returns>False, and no change, when that role already has this font.</returns>
    public bool SetFont(FontRole role, PlatformFont? font, Guid modifiedBy)
    {
        if (Equals(FontFor(role), font))
        {
            return false;
        }

        if (font is null)
        {
            _fonts.Remove(role);
        }
        else
        {
            _fonts[role] = font;
        }

        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
        return true;
    }

    /// <summary>
    /// Whether any role still uses the stored file <paramref name="key"/>: one file may serve
    /// both roles, and a replaced file is deleted only once no role uses it.
    /// </summary>
    public bool UsesFontFile(string key) =>
        _fonts.Values.Any(font => string.Equals(font.Key, key, StringComparison.Ordinal));
}
