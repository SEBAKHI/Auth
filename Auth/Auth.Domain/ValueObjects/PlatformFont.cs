using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// One of the platform's font files: where it is stored, and the family name it declares,
/// shown to administrators only. The pages never use that name in CSS: they register the
/// file under a family name of their own, so a name an uploader chose cannot reach a style.
/// </summary>
public sealed record class PlatformFont
{
    /// <summary>Gets the relative storage key of the WOFF2 file (composed into a URL at the API edge).</summary>
    public string Key { get; }

    /// <summary>
    /// Gets the family name the font declares (or the uploaded file's name when it declares
    /// none), without control characters and at most <see cref="PlatformFonts.MaxFamilyNameLength"/>
    /// characters; null when neither gave one.
    /// </summary>
    public string? FamilyName { get; }

    private PlatformFont(string key, string? familyName)
    {
        Key = key;
        FamilyName = familyName;
    }

    /// <summary>
    /// A stored font. The key is minted by the storage service; the name is cleaned here,
    /// since it comes from a file anyone with the permission can write.
    /// </summary>
    /// <exception cref="ArgumentException">The key is empty: a programming error.</exception>
    public static PlatformFont Create(string key, string? familyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new PlatformFont(key, CleanName(familyName));
    }

    /// <summary>
    /// The size rule of a font file (owner decision D7): something, and at most
    /// <see cref="PlatformFonts.MaxSizeBytes"/>. Checked before the file is read.
    /// </summary>
    public static ErrorOr<Success> CheckFileSize(long lengthBytes) => lengthBytes switch
    {
        <= 0 => FontErrors.FileRequired,
        > PlatformFonts.MaxSizeBytes => FontErrors.FileTooLarge(PlatformFonts.MaxSizeBytes),
        _ => Result.Success,
    };

    /// <summary>Rebuilds a stored font; null when no key is stored.</summary>
    public static PlatformFont? FromStored(string? key, string? familyName) =>
        string.IsNullOrWhiteSpace(key) ? null : new PlatformFont(key, CleanName(familyName));

    private static string? CleanName(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length > PlatformFonts.MaxFamilyNameLength)
        {
            cleaned = cleaned[..PlatformFonts.MaxFamilyNameLength].TrimEnd();
        }

        return cleaned.Length == 0 ? null : cleaned;
    }
}
