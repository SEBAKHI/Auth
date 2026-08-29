using System.Security.Claims;
using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.Administration.Contracts;
using Auth.Application.DTOs;
using Auth.Application.Features.SystemSettings.GetSystemSettings;
using Auth.Application.Features.SystemSettings.ResetSystemSettings;
using Auth.Application.Features.SystemSettings.SendTestEmail;
using Auth.Application.Features.SystemSettings.UpdateSystemSettings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auth.Domain.Constants;

namespace Auth_API.Modules.Administration.Controllers;

/// <summary>
/// Administrative endpoints for the dynamic system settings: reading every
/// configurable appsettings section with effective/override/baseline values,
/// saving per-section overrides, and resetting a section to file values.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/system-settings")]
[Produces("application/json")]
[Authorize]
public class SystemSettingsController : ApiController
{
    private readonly ISender _sender;

    public SystemSettingsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Gets all settings sections with per-field values, sources, and
    /// restart-pending state.
    /// </summary>
    /// <response code="200">Returns the full system-settings view</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions</response>
    [HttpGet]
    [RequirePermission(PermissionCodes.SystemSettings.Manage)]
    [ProducesResponseType(typeof(SystemSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetSystemSettingsQuery(), cancellationToken);

        return result.Match(
            settings => Ok(settings),
            errors => Problem(errors));
    }

    /// <summary>
    /// Replaces one section's overrides. The payload is the complete new
    /// override set; omitted fields revert to configuration-file values.
    /// </summary>
    /// <response code="200">Returns the updated section</response>
    /// <response code="400">Validation failed (unknown/secret/invalid field)</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions</response>
    /// <response code="404">Unknown section</response>
    /// <response code="409">The section changed since it was loaded</response>
    [HttpPut("{sectionKey}")]
    [RequirePermission(PermissionCodes.SystemSettings.Manage)]
    [ProducesResponseType(typeof(SystemSettingsSectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        string sectionKey,
        [FromBody] UpdateSystemSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateSystemSettingsCommand(
            sectionKey,
            request.Overrides,
            request.RowVersion,
            GetUserId());

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            section => Ok(section),
            errors => Problem(errors));
    }

    /// <summary>
    /// Removes every override of a section so it falls back to the
    /// configuration files.
    /// </summary>
    /// <response code="200">Returns the section after the reset</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions</response>
    /// <response code="404">Unknown section</response>
    [HttpPost("{sectionKey}/reset")]
    [RequirePermission(PermissionCodes.SystemSettings.Manage)]
    [ProducesResponseType(typeof(SystemSettingsSectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reset(string sectionKey, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ResetSystemSettingsCommand(sectionKey, GetUserId()), cancellationToken);

        return result.Match(
            section => Ok(section),
            errors => Problem(errors));
    }

    /// <summary>
    /// Sends a diagnostic email to the calling administrator with the
    /// current effective SMTP settings.
    /// </summary>
    /// <response code="204">Test email sent</response>
    /// <response code="400">Email sending is disabled</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions</response>
    [HttpPost("email/test")]
    [RequirePermission(PermissionCodes.SystemSettings.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SendTestEmail(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new SendTestEmailCommand(GetUserId()), cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) ? userId : Guid.Empty;
    }
}
