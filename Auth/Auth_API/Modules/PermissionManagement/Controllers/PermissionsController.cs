using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Modules.PermissionManagement.Commands;
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

    /// <summary>
    /// Get a permission by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("permissions:read")]
    [ProducesResponseType(typeof(PermissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPermission(Guid id)
    {
        var query = new GetPermissionByIdQuery(id);
        var result = await _mediator.Send(query);

        return result.Match(
            permission => Ok(permission),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
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
    public async Task<IActionResult> CreatePermission([FromBody] CreatePermissionRequest request)
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

        var result = await _mediator.Send(command);

        return result.Match(
            permission => CreatedAtAction(nameof(GetPermission), new { id = permission.Id }, permission),
            errors => errors.First().Type == ErrorOr.ErrorType.Conflict
                ? Conflict(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
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
    public async Task<IActionResult> UpdatePermission(Guid id, [FromBody] UpdatePermissionRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new UpdatePermissionCommand(id, request.Name, request.Description)
        {
            ModifiedBy = userId
        };

        var result = await _mediator.Send(command);

        return result.Match(
            permission => Ok(permission),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
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
    public async Task<IActionResult> DeletePermission(Guid id)
    {
        var userId = GetCurrentUserId();
        var command = new DeletePermissionCommand(id) { DeletedBy = userId };
        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
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
    public async Task<IActionResult> GetPermissionImplications(Guid id)
    {
        var query = new GetPermissionImplicationsQuery(id);
        var result = await _mediator.Send(query);

        return result.Match(
            implications => Ok(implications),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
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
    public async Task<IActionResult> AddPermissionImplication(Guid id, [FromBody] AddImplicationRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new AddPermissionImplicationCommand(id, request.ImpliedPermissionId)
        {
            CreatedBy = userId
        };

        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => StatusCode(StatusCodes.Status201Created),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Conflict => Conflict(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Validation => BadRequest(new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
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
    public async Task<IActionResult> RemovePermissionImplication(Guid id, Guid impliedId)
    {
        var userId = GetCurrentUserId();
        var command = new RemovePermissionImplicationCommand(id, impliedId)
        {
            RemovedBy = userId
        };

        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}

// Request DTOs
public record CreatePermissionRequest(
    Guid ApplicationId,
    string Code,
    string Name,
    string? Description = null,
    Guid? ParentId = null);

public record UpdatePermissionRequest(
    string Name,
    string? Description = null);

public record AddImplicationRequest(
    Guid ImpliedPermissionId);
