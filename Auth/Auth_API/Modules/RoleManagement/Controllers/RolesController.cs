using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth.Application.Features.Roles.CreateRole;
using Auth.Application.Features.Roles.DeleteRole;
using Auth.Application.Features.Roles.GetRoleById;
using Auth.Application.Features.Roles.GetRoles;
using Auth.Application.Features.Roles.UpdateRole;
using Auth.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.RoleManagement.Controllers;

/// <summary>
/// Controller for role management operations.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class RolesController : ApiController
{
    private readonly ISender _sender;

    public RolesController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Get all roles for an application.
    /// </summary>
    [HttpGet]
    [RequirePermission("roles:read")]
    [ProducesResponseType(typeof(IReadOnlyList<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRoles([FromQuery] Guid? applicationId = null)
    {
        var query = new GetRolesQuery(applicationId);
        var result = await _sender.Send(query);

        return result.Match(
            roles => Ok(roles),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get a role by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("roles:read")]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRole(Guid id)
    {
        var query = new GetRoleByIdQuery(id);
        var result = await _sender.Send(query);

        return result.Match(
            role => Ok(role),
            errors => Problem(errors));
    }

    /// <summary>
    /// Create a new role.
    /// </summary>
    [HttpPost]
    [RequirePermission("roles:create")]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new CreateRoleCommand(
            request.ApplicationId,
            request.Code,
            request.Name,
            request.Description,
            request.PermissionIds)
        {
            CreatedBy = userId
        };

        var result = await _sender.Send(command);

        return result.Match(
            role => CreatedAtAction(nameof(GetRole), new { id = role.Id }, role),
            errors => Problem(errors));
    }

    /// <summary>
    /// Update an existing role.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("roles:update")]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UpdateRoleRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new UpdateRoleCommand(id, request.Name, request.Description)
        {
            ModifiedBy = userId
        };

        var result = await _sender.Send(command);

        return result.Match(
            role => Ok(role),
            errors => Problem(errors));
    }

    /// <summary>
    /// Delete a role.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("roles:delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteRole(Guid id)
    {
        var userId = GetCurrentUserId();
        var command = new DeleteRoleCommand(id) { DeletedBy = userId };
        var result = await _sender.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}

// Request DTOs
public record CreateRoleRequest(
    Guid ApplicationId,
    string Code,
    string Name,
    string? Description = null,
    IReadOnlyList<Guid>? PermissionIds = null);

public record UpdateRoleRequest(
    string Name,
    string? Description = null);
