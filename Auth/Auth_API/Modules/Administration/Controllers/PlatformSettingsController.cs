using System.Security.Claims;
using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.Administration.Contracts;
using Auth.Application.DTOs;
using Auth.Application.Features.Platform.GetPlatformSettings;
using Auth.Application.Features.Platform.UpdatePlatformSettings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    [RequirePermission("platform-settings:manage")]
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
    [RequirePermission("platform-settings:manage")]
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

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) ? userId : Guid.Empty;
    }
}
