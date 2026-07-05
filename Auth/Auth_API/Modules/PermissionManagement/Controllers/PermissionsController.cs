using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.PermissionManagement.Contracts;
using Auth.Application.Features.Permissions.AddPermissionImplication;
using Auth.Application.Features.Permissions.CreatePermission;
using Auth.Application.Features.Permissions.DeletePermission;
using Auth.Application.Features.Permissions.GetPermissionById;
using Auth.Application.Features.Permissions.GetPermissionImplications;
using Auth.Application.Features.Permissions.GetPermissions;
using Auth.Application.Features.Permissions.GetPermissionUsers;
using Auth.Application.Features.Permissions.RemovePermissionImplication;
using Auth.Application.Features.Permissions.UpdatePermission;
using Auth.Application.DTOs;
using Auth.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.PermissionManagement.Controllers;

/// <summary>
/// Controller for permission management operations.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class PermissionsController : ApiController
{
    private readonly ISender _sender;

    public PermissionsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Get all permissions for an application.
    /// </summary>
    [HttpGet]
    [RequirePermission("permissions:read")]
    [ProducesResponseType(typeof(IReadOnlyList<PermissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPermissions(
        [FromQuery] Guid? applicationId = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetPermissionsQuery(applicationId, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            permissions => Ok(permissions),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get a permission by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("permissions:read")]
    [ProducesResponseType(typeof(PermissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPermission(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetPermissionByIdQuery(id);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            permission => Ok(permission),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get paginated users granted a permission.
    /// </summary>
    [HttpGet("{id:guid}/users")]
    [RequirePermission("permissions:read")]
    [ProducesResponseType(typeof(PagedPermissionUsersDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPermissionUsers(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetPermissionUsersQuery(id, pageNumber, pageSize, search, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            users => Ok(users),
            errors => Problem(errors));
    }

    /// <summary>
    /// Create a new permission.
    /// </summary>
    [HttpPost]
    [RequirePermission("permissions:create")]
    [ProducesResponseType(typeof(PermissionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreatePermission([FromBody] CreatePermissionRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new CreatePermissionCommand(
            request.ApplicationId,
            request.Code,
            request.Name,
            request.Description,
            request.ParentId)
        {
            CreatedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            permission => CreatedAtAction(nameof(GetPermission), new { id = permission.Id }, permission),
            errors => Problem(errors));
    }

    /// <summary>
    /// Update an existing permission.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("permissions:update")]
    [ProducesResponseType(typeof(PermissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdatePermission(Guid id, [FromBody] UpdatePermissionRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new UpdatePermissionCommand(id, request.Name, request.Description)
        {
            ModifiedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            permission => Ok(permission),
            errors => Problem(errors));
    }

    /// <summary>
    /// Delete a permission.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("permissions:delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeletePermission(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new DeletePermissionCommand(id) { DeletedBy = userId };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get all permissions implied by a permission.
    /// </summary>
    [HttpGet("{id:guid}/implications")]
    [RequirePermission("permissions:read")]
    [ProducesResponseType(typeof(IReadOnlyList<PermissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPermissionImplications(
        Guid id,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetPermissionImplicationsQuery(id, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            implications => Ok(implications),
            errors => Problem(errors));
    }

    /// <summary>
    /// Add a permission implication (permission A implies permission B).
    /// </summary>
    [HttpPost("{id:guid}/implications")]
    [RequirePermission("permissions:manage")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AddPermissionImplication(Guid id, [FromBody] AddImplicationRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new AddPermissionImplicationCommand(id, request.ImpliedPermissionId)
        {
            CreatedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => StatusCode(StatusCodes.Status201Created),
            errors => Problem(errors));
    }

    /// <summary>
    /// Remove a permission implication.
    /// </summary>
    [HttpDelete("{id:guid}/implications/{impliedId:guid}")]
    [RequirePermission("permissions:manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemovePermissionImplication(Guid id, Guid impliedId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new RemovePermissionImplicationCommand(id, impliedId)
        {
            RemovedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

}
