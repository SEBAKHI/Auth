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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
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
        var result = await _storage.SaveImageAsync(stream, file.ContentType, cancellationToken);

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
}

/// <summary>Response for a successful image upload.</summary>
public record UploadImageResponse(string Key, string Url);
