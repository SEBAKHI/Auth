using Auth.Application.Configuration;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Media.Filters;

/// <summary>
/// Applies <c>ImageStorage:MaxSizeBytes</c> as the transport-level request body
/// limit for image uploads.
/// <para>
/// A <c>[RequestSizeLimit]</c> attribute cannot do this: it is a compile-time
/// constant, so the console's editable maximum and the pipeline's real ceiling
/// drift apart — a saved value above the constant was silently unreachable (the
/// request was aborted before the handler's friendly size check ever ran), and a
/// value below it still let the whole oversized body be buffered first.
/// </para>
/// <para>
/// This runs as a RESOURCE filter, which executes before model binding: once the
/// multipart form has been read the limit can no longer be changed. Reading the
/// value through <see cref="IOptionsSnapshot{T}"/> keeps it hot, so a limit saved
/// in the console applies to the very next upload.
/// </para>
/// </summary>
public sealed class ImageUploadSizeLimitFilter : IAsyncResourceFilter
{
    /// <summary>
    /// Slack for the multipart envelope (boundaries, part headers, filename) so a
    /// file of exactly MaxSizeBytes is not rejected by the bytes wrapping it. The
    /// handler still enforces the exact limit against the file's own length.
    /// </summary>
    private const long MultipartEnvelopeAllowanceBytes = 8 * 1024;

    private readonly IOptionsSnapshot<ImageStorageSettings> _settings;

    public ImageUploadSizeLimitFilter(IOptionsSnapshot<ImageStorageSettings> settings)
        => _settings = settings;

    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next)
    {
        var feature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();

        // Absent on HTTP/2+ and when the server does not support per-request
        // limits, and read-only once the body has started — neither is an error
        // here, it just means the host-wide limit governs.
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize =
                _settings.Value.MaxSizeBytes + MultipartEnvelopeAllowanceBytes;
        }

        await next();
    }
}
