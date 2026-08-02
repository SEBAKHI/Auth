namespace Auth.Application.Configuration;

/// <summary>
/// Settings for image storage (user profile images, organization/application logos).
/// The filesystem provider writes files under <see cref="PhysicalPath"/> and serves them
/// at <see cref="RequestPath"/>; the database stores an opaque relative key and the API
/// composes the absolute URL from <see cref="PublicBaseUrl"/> at read time — so changing
/// host/scheme (or moving to object storage + CDN) never rewrites stored data.
/// </summary>
public class ImageStorageSettings
{
    public const string SectionName = "ImageStorage";

    /// <summary>Storage backend. Only "filesystem" is implemented today; object storage is a drop-in later.</summary>
    public string Provider { get; set; } = "filesystem";

    /// <summary>Absolute path, or a path relative to the app base directory, where image files are written.</summary>
    public string PhysicalPath { get; set; } = "uploads/images";

    /// <summary>Public URL prefix used to compose absolute image URLs (e.g. https://host/uploads/images).</summary>
    public string PublicBaseUrl { get; set; } = "/uploads/images";

    /// <summary>Static request path the files are served at (e.g. /uploads/images).</summary>
    public string RequestPath { get; set; } = "/uploads/images";

    /// <summary>Maximum accepted upload size in bytes (default 4 MB — profile pictures and logos are small).</summary>
    public long MaxSizeBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// Maximum decoded image size in megapixels. Checked from the image header
    /// BEFORE decoding pixels, so a small but huge-dimensioned "decompression
    /// bomb" cannot force a multi-gigabyte allocation (width*height*4 bytes).
    /// </summary>
    public int MaxMegapixels { get; set; } = 50;

    /// <summary>Max width/height in pixels; larger images are resized down preserving aspect ratio.</summary>
    public int MaxEdgePx { get; set; } = 1024;

    /// <summary>WebP encode quality (0-100) for the re-encoded image.</summary>
    public int WebpQuality { get; set; } = 90;

    /// <summary>
    /// Accepted upload content types used when configuration supplies none at all
    /// (SVG deliberately excluded — it is an XSS vector). Applied by
    /// <see cref="SettingsArrayNormalizer"/> AFTER binding, never as the property
    /// initializer: the configuration binder APPENDS configured entries to whatever
    /// array the property already holds, so an initializer would make these four
    /// types permanently unremovable from any configuration layer.
    /// </summary>
    public static readonly string[] DefaultAllowedContentTypes =
        ["image/png", "image/jpeg", "image/webp", "image/gif"];

    /// <summary>
    /// Accepted upload content types. Starts empty on purpose;
    /// <see cref="DefaultAllowedContentTypes"/> is substituted after binding only
    /// when configuration provides nothing.
    /// </summary>
    public string[] AllowedContentTypes { get; set; } = [];
}
