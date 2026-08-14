using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.RoleManagement.Contracts;
using Auth.Application.Features.Roles.CreateRole;
using Auth.Application.Features.Roles.DeleteRole;
using Auth.Application.Features.Roles.GetRoleApplications;
using Auth.Application.Features.Roles.GetRoleById;
using Auth.Application.Features.Roles.GetRoles;
using Auth.Application.Features.Roles.GetRoleUsers;
using Auth.Application.Features.Roles.UpdateRole;
using Auth.Application.DTOs;
using Auth.Domain.Enums;
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
    public async Task<IActionResult> GetRoles(
        [FromQuery] Guid? applicationId = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetRolesQuery(applicationId, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

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
    public async Task<IActionResult> GetRole(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetRoleByIdQuery(id);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            role => Ok(role),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get paginated users assigned a role.
    /// </summary>
    [HttpGet("{id:guid}/users")]
    [RequirePermission("roles:read")]
    [ProducesResponseType(typeof(PagedRoleUsersDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRoleUsers(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetRoleUsersQuery(id, pageNumber, pageSize, searchTerm, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            users => Ok(users),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get the applications related to a role.
    /// </summary>
    [HttpGet("{id:guid}/applications")]
    [RequirePermission("roles:read")]
    [ProducesResponseType(typeof(IReadOnlyList<RoleApplicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRoleApplications(
        Guid id,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetRoleApplicationsQuery(id, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            applications => Ok(applications),
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
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
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

        var result = await _sender.Send(command, cancellationToken);

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
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new UpdateRoleCommand(id, request.Name, request.Description)
        {
            ModifiedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

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
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new DeleteRoleCommand(id) { DeletedBy = userId };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

}
