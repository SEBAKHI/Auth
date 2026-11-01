using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Auth_API.Modules.Media;

/// <summary>
/// How the uploads volume (<c>ImageStorage:PhysicalPath</c>) is served: profile pictures,
/// logos, email logo renditions and the platform's fonts. Every response is <c>nosniff</c>;
/// the request path is exempted from gateway-token validation (<c>Gateway:ExemptPaths</c>).
/// </summary>
public static class UploadsStaticFiles
{
    private const string Woff2Extension = ".woff2";

    /// <summary>The static-files options of the uploads volume at <paramref name="root"/>.</summary>
    public static StaticFileOptions Options(string root, string requestPath) => new()
    {
        FileProvider = new PhysicalFileProvider(root),
        RequestPath = requestPath,
        ContentTypeProvider = ContentTypes(),
        OnPrepareResponse = ctx => PrepareResponse(ctx.File.Name, ctx.Context.Response.Headers),
    };

    private static void PrepareResponse(string fileName, IHeaderDictionary headers)
    {
        headers["X-Content-Type-Options"] = "nosniff";

        // The platform's fonts (fonts/{sha-256}.woff2): every @font-face fetch is a CORS
        // request, from both apps' origins and from the sign-in page design frame, whose origin
        // is opaque ("null"). Without this header the browser drops the font silently and the
        // page falls back to IBM Plex. Named by their content, so they never change.
        if (fileName.EndsWith(Woff2Extension, StringComparison.OrdinalIgnoreCase))
        {
            headers["Access-Control-Allow-Origin"] = "*";
            headers["Cache-Control"] = "public, max-age=31536000, immutable";
            return;
        }

        // Uploads are content-addressed by a random key, so they are immutable and cache
        // hard. The email logo renditions are the exception: they sit at a STABLE filename
        // that is overwritten whenever branding changes, precisely so that mail already
        // delivered picks up the new logo. A long TTL there would defeat the point, so they
        // get a short one and must be revalidated.
        var isEmailRendition = fileName.StartsWith("platform-email-", StringComparison.OrdinalIgnoreCase);
        headers["Cache-Control"] = isEmailRendition
            ? "public, max-age=3600, must-revalidate"
            : "public, max-age=86400";
    }

    /// <summary>
    /// The framework's table, except that a WOFF2 file is stated as <c>font/woff2</c> (RFC 8081)
    /// here rather than left to whatever a framework version maps it to: with <c>nosniff</c>,
    /// the type is what the browser goes by.
    /// </summary>
    private static FileExtensionContentTypeProvider ContentTypes()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[Woff2Extension] = "font/woff2";
        return provider;
    }
}
