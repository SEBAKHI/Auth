using Auth.Domain.Interfaces.Repositories;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth_API.Modules.Media.Filters;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
public class ImagesController : ControllerBase
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

    /// <summary>
    /// The caller, as the uploads ledger records them.
    /// </summary>
    /// <remarks>
    /// Same claim <c>ApiController.GetCurrentUserId</c> reads. Duplicated rather
    /// than inherited because this controller extends ControllerBase directly
    /// and changing its base would change its error-shaping too.
    /// </remarks>
    private Guid GetCurrentUserId()
        => Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) ? userId : Guid.Empty;

    /// <summary>Uploads and processes an image; returns its storage key and public URL.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    // The body limit follows ImageStorage:MaxSizeBytes live; a constant here would
    // put a second, invisible ceiling under the one the console publishes.
    [ServiceFilter(typeof(ImageUploadSizeLimitFilter))]
    [ProducesResponseType(typeof(UploadImageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "No file provided." });
        }

        if (file.Length > _settings.MaxSizeBytes)
        {
            return BadRequest(new { error = $"File exceeds the maximum size of {_settings.MaxSizeBytes} bytes." });
        }

        // The per-file limit above bounds one request; nothing bounded the sum of
        // them, so any authenticated user could fill the uploads volume four
        // megabytes at a time. On shared hosting that stops the whole tenant, not
        // just uploading.
        var uploaderId = GetCurrentUserId();
        var usedBytes = await _uploadedImages.GetUsedBytesAsync(uploaderId, cancellationToken);
        if (usedBytes + file.Length > _settings.MaxBytesPerUser)
        {
            return BadRequest(new
            {
                error = $"Storage quota reached: {usedBytes} of {_settings.MaxBytesPerUser} bytes used. "
                    + "Remove an image you no longer need, or ask an administrator to raise the quota."
            });
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
            errors => Task.FromResult<IActionResult>(errors[0].Type == ErrorType.Unexpected
                // Storage/environment fault (e.g. the uploads directory is not writable) — a
                // server fault, not a problem with the uploaded file.
                ? StatusCode(StatusCodes.Status500InternalServerError, new { error = errors[0].Description })
                : BadRequest(new { error = errors[0].Description })));
    }
}

/// <summary>Response for a successful image upload.</summary>
public record UploadImageResponse(string Key, string Url);
