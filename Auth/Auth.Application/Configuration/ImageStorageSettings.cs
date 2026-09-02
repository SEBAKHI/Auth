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
    /// Total bytes one user may occupy across all their uploads. The per-file
    /// limit above bounds a single request; this bounds the sum of them.
    /// </summary>
    public long MaxBytesPerUser { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// How long an upload may sit unattached before the cleanup sweep reclaims
    /// it. Long enough that a slow form does not lose its image, short enough
    /// that abandoned ones do not accumulate.
    /// </summary>
    public int OrphanRetentionHours { get; set; } = 24;

    /// <summary>
    /// Maximum decoded image size in megapixels. Checked from the image header
    /// BEFORE decoding pixels, so a small but huge-dimensioned "decompression
    /// bomb" cannot force a multi-gigabyte allocation (width*height*4 bytes).
    /// </summary>
    /// <remarks>
    /// A memory budget, not a compatibility ceiling: every megapixel admitted
    /// here costs 4 MB of process memory for the whole decode, on the request
    /// thread, however small the compressed file was. 24 admits the largest
    /// common camera output (the default of current phones and APS-C cameras)
    /// at ~96 MB per decode; the previous 50 admitted ~200 MB per decode for
    /// an output that never exceeds <see cref="MaxEdgePx"/> anyway. Read it
    /// together with RateLimiting:ImageUploadConcurrencyLimit — the two multiply.
    /// </remarks>
    public int MaxMegapixels { get; set; } = 24;

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
