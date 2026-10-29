using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth_API.Common;
using Auth_API.Modules.Media.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Media.Controllers;

/// <summary>
/// Generic authenticated image upload. Validates size + type, delegates processing/storage
/// to <see cref="IImageStorageService"/> (re-encode/resize/strip-metadata), and returns the
/// storage key plus its composed URL. The caller then persists the key onto the target entity
/// (user profile image, organization/application logo).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ImagesController : ApiController
{
    private readonly IImageStorageService _storage;
    private readonly IImageUrlComposer _urlComposer;
    private readonly IUploadedImageRepository _uploadedImages;
    private readonly ImageStorageSettings _settings;

    public ImagesController(
        IImageStorageService storage,
        IImageUrlComposer urlComposer,
        IUploadedImageRepository uploadedImages,
        IOptionsSnapshot<ImageStorageSettings> settings)
    {
        _storage = storage;
        _urlComposer = urlComposer;
        _uploadedImages = uploadedImages;
        _settings = settings.Value;
    }

    /// <summary>Uploads and processes an image; returns its storage key and public URL.</summary>
    /// <param name="file">The image.</param>
    /// <param name="purpose">
    /// What it is for: <c>Panel</c> for an image inserted into a sign-in page design, which keeps
    /// up to <c>ImageStorage:PanelMaxEdgePx</c>; left out, every other upload's limit.
    /// <c>Panel</c> is for the people who edit designs: holders of <c>applications:update</c>
    /// (an application's designs) or <c>platform-settings:manage</c> (the platform's); anyone
    /// else is refused with 403 <c>Auth.Unauthorized</c>.
    /// </param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [Consumes("multipart/form-data")]
    // The body limit follows ImageStorage:MaxSizeBytes live; a constant here would
    // put a second, invisible ceiling under the one the console publishes.
    [ServiceFilter(typeof(ImageUploadSizeLimitFilter))]
    // Concurrency, not a window: the decode behind this action holds
    // ImageStorage:MaxMegapixels x 4 MB per in-flight request. See the policy.
    [EnableRateLimiting("image-upload")]
    [ProducesResponseType(typeof(UploadImageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromQuery] ImageUploadPurpose purpose = ImageUploadPurpose.Default,
        CancellationToken cancellationToken = default)
    {
        // Every signed-in person uploads their photo and their organization's logo here, so the
        // endpoint itself stays open to them. A larger allowance is not theirs: it is for the
        // design editors, the only place that asks for it. Any purpose but the default is
        // refused first, before the file or the quota is read; a purpose added later inherits
        // the refusal until someone decides who it is for.
        if (purpose != ImageUploadPurpose.Default && !MayEditSignInPageDesigns())
        {
            return Problem([AuthErrors.Unauthorized]);
        }

        if (file is null || file.Length == 0)
        {
            return Problem([ImageErrors.FileRequired]);
        }

        if (file.Length > _settings.MaxSizeBytes)
        {
            return Problem([ImageErrors.FileTooLarge(_settings.MaxSizeBytes)]);
        }

        // The per-file limit above bounds one request; nothing bounded the sum of
        // them, so any authenticated user could fill the uploads volume four
        // megabytes at a time. On shared hosting that stops the whole tenant, not
        // just uploading.
        var uploaderId = GetCurrentUserId();
        var usedBytes = await _uploadedImages.GetUsedBytesAsync(uploaderId, cancellationToken);
        if (usedBytes + file.Length > _settings.MaxBytesPerUser)
        {
            return Problem([ImageErrors.QuotaExceeded(usedBytes, _settings.MaxBytesPerUser)]);
        }

        await using var stream = file.OpenReadStream();
        var result = await _storage.SaveImageAsync(stream, file.ContentType, purpose, cancellationToken);

        return await result.Match<Task<IActionResult>>(
            async key =>
            {
                // Measured after the write, not before it: what fills the volume is
                // the re-encoded WebP, and file.Length is the bytes the client sent.
                var storedBytes = await _storage.GetStoredSizeAsync(key, cancellationToken) ?? file.Length;
                await _uploadedImages.RecordAsync(key, uploaderId, storedBytes, cancellationToken);

                return Ok(new UploadImageResponse(key, _urlComposer.Compose(key)!));
            },
            // A storage fault (Image.StorageUnavailable, Unexpected) maps to 500, a rejected
            // file to 400: the status map decides, as for every handler error.
            errors => Task.FromResult(Problem(errors)));
    }

    /// <summary>
    /// The permissions that save a sign-in page design: an application's
    /// (<c>applications:update</c>) or the platform's (<c>platform-settings:manage</c>), read
    /// from the token with the same wildcard rules as <c>[RequirePermission]</c>.
    /// </summary>
    private bool MayEditSignInPageDesigns() =>
        HasPermissionClaim(PermissionCodes.Applications.Update)
        || HasPermissionClaim(PermissionCodes.PlatformSettings.Manage);
}

/// <summary>Response for a successful image upload.</summary>
public record UploadImageResponse(string Key, string Url);
