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

    /// <summary>Maximum accepted upload size in bytes (default 10 MB).</summary>
    public long MaxSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Max width/height in pixels; larger images are resized down preserving aspect ratio.</summary>
    public int MaxEdgePx { get; set; } = 1024;

    /// <summary>WebP encode quality (0-100) for the re-encoded image.</summary>
    public int WebpQuality { get; set; } = 90;

    /// <summary>Accepted upload content types (SVG deliberately excluded — it is an XSS vector).</summary>
    public string[] AllowedContentTypes { get; set; } =
        ["image/png", "image/jpeg", "image/webp", "image/gif"];
}
