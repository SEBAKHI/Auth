using System.Security.Claims;
using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.Administration.Contracts;
using Auth.Application.DTOs;
using Auth.Application.Features.Platform.GetPlatformSettings;
using Auth.Application.Features.Platform.SetPlatformFont;
using Auth.Application.Features.Platform.UpdatePlatformHiddenLanguages;
using Auth.Application.Features.Platform.UpdatePlatformLogoHeight;
using Auth.Application.Features.Platform.UpdatePlatformSettings;
using Auth.Application.Features.Platform.UpdatePlatformTheme;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Errors;

namespace Auth_API.Modules.Administration.Controllers;

/// <summary>
/// Administrative endpoints for managing the platform branding settings
/// (platform name and logo shown across the console and auth screens).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/platform-settings")]
[Produces("application/json")]
[Authorize]
public class PlatformSettingsController : ApiController
{
    private readonly ISender _sender;

    public PlatformSettingsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Gets the full platform settings, including modification audit fields.
    /// </summary>
    /// <response code="200">Returns the platform settings</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions</response>
    [HttpGet]
    [RequirePermission(PermissionCodes.PlatformSettings.Manage)]
    [ProducesResponseType(typeof(PlatformSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetPlatformSettingsQuery(), cancellationToken);

        return result.Match(
            settings => Ok(settings),
            errors => Problem(errors));
    }

    /// <summary>
    /// Updates the platform branding settings.
    /// </summary>
    /// <response code="200">Returns the updated platform settings</response>
    /// <response code="400">Validation failed</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions</response>
    [HttpPut]
    [RequirePermission(PermissionCodes.PlatformSettings.Manage)]
    [ProducesResponseType(typeof(PlatformSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(
        [FromBody] UpdatePlatformSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdatePlatformSettingsCommand(
            request.PlatformName,
            request.LogoUrl,
            request.LogoUrlDark,
            request.FaviconUrl,
            GetUserId());

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            settings => Ok(settings),
            errors => Problem(errors));
    }

    /// <summary>
    /// Replaces the platform appearance (base colour, theme, chart colour,
    /// radius, menu accent, control height and table row height). Name, logos
    /// and favicon are left as they are.
    /// </summary>
    /// <response code="200">Returns the updated platform settings</response>
    /// <response code="400">An unknown name, a theme not available for the base colour, a malformed custom colour, a custom height out of bounds, or a row lower than the controls</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions</response>
    [HttpPut("theme")]
    [RequirePermission(PermissionCodes.PlatformSettings.Manage)]
    [ProducesResponseType(typeof(PlatformSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateTheme(
        [FromBody] UpdatePlatformThemeRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdatePlatformThemeCommand(
            request.Base.ToChoice(),
            request.Theme.ToChoice(),
            request.Chart.ToChoice(),
            request.Radius,
            request.MenuAccent,
            request.ControlSize.ToSize(),
            request.TableRowHeight.ToSize(),
            GetUserId());

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            settings => Ok(settings),
            errors => Problem(errors));
    }

    /// <summary>
    /// Sets the height the platform's logo is shown at on the sign-in pages: the area it takes
    /// on the page, never the image. Name, logos, favicon and appearance are left as they are.
    /// </summary>
    /// <response code="200">Returns the updated platform settings</response>
    /// <response code="400">A height outside 24–120 pixels</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions</response>
    [HttpPut("logo-height")]
    [RequirePermission(PermissionCodes.PlatformSettings.Manage)]
    [ProducesResponseType(typeof(PlatformSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateLogoHeight(
        [FromBody] UpdateLogoHeightRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new UpdatePlatformLogoHeightCommand(request.LogoHeightPx, GetUserId()),
            cancellationToken);

        return result.Match(
            settings => Ok(settings),
            errors => Problem(errors));
    }

    /// <summary>
    /// Sets the languages hidden from the language menus of both apps: a hidden language is
    /// neither listed nor downloaded, and a page that would open in it opens in English. Content
    /// editors keep every language. Nothing else in the settings changes.
    /// </summary>
    /// <response code="200">Returns the updated platform settings</response>
    /// <response code="400">A code that is not a shipped language, or English</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions</response>
    [HttpPut("hidden-languages")]
    [RequirePermission(PermissionCodes.PlatformSettings.Manage)]
    [ProducesResponseType(typeof(PlatformSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateHiddenLanguages(
        [FromBody] UpdateHiddenLanguagesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new UpdatePlatformHiddenLanguagesCommand(request.HiddenLanguages, GetUserId()),
            cancellationToken);

        return result.Match(
            settings => Ok(settings),
            errors => Problem(errors));
    }

    /// <summary>
    /// Slack for the multipart envelope (boundaries, part headers, file name), so a font of
    /// exactly <see cref="PlatformFonts.MaxSizeBytes"/> is not refused by the bytes around it.
    /// The handler checks the file's own length against the exact limit.
    /// </summary>
    private const long FontUploadBodyLimit = PlatformFonts.MaxSizeBytes + (8 * 1024);

    /// <summary>
    /// Sets one of the platform's fonts from a WOFF2 file of at most 2 MiB: <c>heading</c>
    /// (titles) or <c>body</c> (everything else). Both apps and every sign-in page design use
    /// it at once; nothing else in the settings changes. The file is kept as uploaded, after a
    /// check that it is a whole WOFF2 font.
    /// </summary>
    /// <param name="role">Which font: <c>heading</c> or <c>body</c>.</param>
    /// <param name="file">The WOFF2 file.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    /// <response code="200">Returns the updated platform settings</response>
    /// <response code="400">No file, a file over 2 MiB, or a file that is not a whole WOFF2 font</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions</response>
    [HttpPut("fonts/{role:regex(^(heading|body)$)}")]
    [RequirePermission(PermissionCodes.PlatformSettings.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(FontUploadBodyLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = FontUploadBodyLimit)]
    // Concurrency, as for images: checking a font holds its decompressed tables in memory.
    [EnableRateLimiting("image-upload")]
    [ProducesResponseType(typeof(PlatformSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetFont(
        FontRole role,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Problem([FontErrors.FileRequired]);
        }

        await using var content = file.OpenReadStream();
        var result = await _sender.Send(
            new SetPlatformFontCommand(role, new FontUpload(content, file.Length, file.FileName), GetUserId()),
            cancellationToken);

        return result.Match(
            settings => Ok(settings),
            errors => Problem(errors));
    }

    /// <summary>
    /// Removes one of the platform's fonts: its role goes back to the shipped IBM Plex Sans (a
    /// heading without its own font follows the body's). Removing a font that is not set
    /// changes nothing.
    /// </summary>
    /// <param name="role">Which font: <c>heading</c> or <c>body</c>.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    /// <response code="200">Returns the updated platform settings</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions</response>
    [HttpDelete("fonts/{role:regex(^(heading|body)$)}")]
    [RequirePermission(PermissionCodes.PlatformSettings.Manage)]
    [ProducesResponseType(typeof(PlatformSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveFont(FontRole role, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new SetPlatformFontCommand(role, null, GetUserId()), cancellationToken);

        return result.Match(
            settings => Ok(settings),
            errors => Problem(errors));
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) ? userId : Guid.Empty;
    }
}
