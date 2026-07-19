using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
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
    private readonly ImageStorageSettings _settings;

    public ImagesController(
        IImageStorageService storage,
        IImageUrlComposer urlComposer,
        IOptions<ImageStorageSettings> settings)
    {
        _storage = storage;
        _urlComposer = urlComposer;
        _settings = settings.Value;
    }

    /// <summary>Uploads and processes an image; returns its storage key and public URL.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5 * 1024 * 1024)]
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

        await using var stream = file.OpenReadStream();
        var result = await _storage.SaveImageAsync(stream, file.ContentType, cancellationToken);

        return result.Match<IActionResult>(
            key => Ok(new UploadImageResponse(key, _urlComposer.Compose(key)!)),
            errors => errors[0].Type == ErrorType.Unexpected
                // Storage/environment fault (e.g. the uploads directory is not writable) — a
                // server fault, not a problem with the uploaded file.
                ? StatusCode(StatusCodes.Status500InternalServerError, new { error = errors[0].Description })
                : BadRequest(new { error = errors[0].Description }));
    }
}

/// <summary>Response for a successful image upload.</summary>
public record UploadImageResponse(string Key, string Url);
