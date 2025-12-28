using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Modules.PermissionManagement.Queries;
using Auth_Lib.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.PermissionManagement.Controllers;

/// <summary>
/// Controller for permission management operations.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class PermissionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PermissionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all permissions for an application.
    /// </summary>
    [HttpGet]
    [RequirePermission("permissions:read")]
    [ProducesResponseType(typeof(IReadOnlyList<PermissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPermissions([FromQuery] Guid? applicationId = null)
    {
        var query = new GetPermissionsQuery(applicationId);
        var result = await _mediator.Send(query);

        return result.Match(
            permissions => Ok(permissions),
            errors => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: errors.First().Description));
    }
}
