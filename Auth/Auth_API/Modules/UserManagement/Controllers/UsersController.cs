using Asp.Versioning;
using Auth_API.Authorization;
using Auth_Lib.Application.Features.Users.ActivateAccount;
using Auth_Lib.Application.Features.Users.AssignRole;
using Auth_Lib.Application.Features.Users.CreateUser;
using Auth_Lib.Application.Features.Users.DeactivateAccount;
using Auth_Lib.Application.Features.Users.DeleteUser;
using Auth_Lib.Application.Features.Users.GetUserById;
using Auth_Lib.Application.Features.Users.GetUserPermissions;
using Auth_Lib.Application.Features.Users.GetUserRoles;
using Auth_Lib.Application.Features.Users.GetUsers;
using Auth_Lib.Application.Features.Users.GrantUserPermission;
using Auth_Lib.Application.Features.Users.LockAccount;
using Auth_Lib.Application.Features.Users.RemoveUserRole;
using Auth_Lib.Application.Features.Users.RevokeUserPermission;
using Auth_Lib.Application.Features.Users.UnlockAccount;
using Auth_Lib.Application.Features.Users.UpdateProfile;
using Auth_Lib.Application.Features.Users.UpdateUser;
using Auth_Lib.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.UserManagement.Controllers;

/// <summary>
/// Controller for user management operations.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all users with pagination.
    /// </summary>
    [HttpGet]
    [RequirePermission("users:read")]
    [ProducesResponseType(typeof(PagedUsersDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? searchTerm = null)
    {
        var query = new GetUsersQuery(pageNumber, pageSize, searchTerm);
        var result = await _mediator.Send(query);

        return result.Match(
            users => Ok(users),
            errors => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: errors.First().Description));
    }

    /// <summary>
    /// Get a user by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("users:read")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUser(Guid id)
    {
        var query = new GetUserByIdQuery(id);
        var result = await _mediator.Send(query);

        return result.Match(
            user => Ok(user),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Create a new user.
    /// </summary>
    [HttpPost]
    [RequirePermission("users:create")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new CreateUserCommand(
            request.Email,
            request.Password,
            request.FirstName,
            request.LastName,
            request.DisplayName,
            request.PhoneNumber,
            request.PreferredLanguage,
            request.TimeZone,
            request.RoleIds)
        {
            CreatedBy = userId
        };

        var result = await _mediator.Send(command);

        return result.Match(
            user => CreatedAtAction(nameof(GetUser), new { id = user.Id }, user),
            errors => errors.First().Type == ErrorOr.ErrorType.Conflict
                ? Conflict(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Update an existing user.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("users:update")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new UpdateUserCommand(
            id,
            request.FirstName,
            request.LastName,
            request.DisplayName,
            request.PhoneNumber,
            request.PreferredLanguage,
            request.TimeZone)
        {
            ModifiedBy = userId
        };

        var result = await _mediator.Send(command);

        return result.Match(
            user => Ok(user),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Delete a user.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("users:delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var userId = GetCurrentUserId();
        var command = new DeleteUserCommand(id) { DeletedBy = userId };
        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Assign a role to a user.
    /// </summary>
    [HttpPost("{id:guid}/roles")]
    [RequirePermission("users:manage-roles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AssignRole(Guid id, [FromBody] AssignRoleRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new AssignRoleCommand(id, request.RoleId, request.ExpiresAt)
        {
            AssignedBy = userId
        };

        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Conflict => Conflict(new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    /// <summary>
    /// Get all roles assigned to a user.
    /// </summary>
    [HttpGet("{id:guid}/roles")]
    [RequirePermission("users:read")]
    [ProducesResponseType(typeof(IReadOnlyList<UserRoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUserRoles(Guid id)
    {
        var query = new GetUserRolesQuery(id);
        var result = await _mediator.Send(query);

        return result.Match(
            roles => Ok(roles),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Remove a role from a user.
    /// </summary>
    [HttpDelete("{id:guid}/roles/{roleId:guid}")]
    [RequirePermission("users:manage-roles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveRole(Guid id, Guid roleId)
    {
        var userId = GetCurrentUserId();
        var command = new RemoveUserRoleCommand(id, roleId) { RemovedBy = userId };
        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Get all direct permissions granted to a user.
    /// </summary>
    [HttpGet("{id:guid}/permissions")]
    [RequirePermission("users:read")]
    [ProducesResponseType(typeof(IReadOnlyList<UserPermissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUserPermissions(Guid id)
    {
        var query = new GetUserPermissionsQuery(id);
        var result = await _mediator.Send(query);

        return result.Match(
            permissions => Ok(permissions),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Grant a permission directly to a user.
    /// </summary>
    [HttpPost("{id:guid}/permissions")]
    [RequirePermission("users:manage-permissions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GrantPermission(Guid id, [FromBody] GrantPermissionRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new GrantUserPermissionCommand(id, request.PermissionId, request.ApplicationId, request.ExpiresAt)
        {
            GrantedBy = userId
        };

        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Conflict => Conflict(new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    /// <summary>
    /// Revoke a permission from a user.
    /// </summary>
    [HttpDelete("{id:guid}/permissions/{permissionId:guid}")]
    [RequirePermission("users:manage-permissions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevokePermission(Guid id, Guid permissionId)
    {
        var userId = GetCurrentUserId();
        var command = new RevokeUserPermissionCommand(id, permissionId) { RevokedBy = userId };
        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Lock a user account.
    /// </summary>
    [HttpPost("{id:guid}/lock")]
    [RequirePermission("users:manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> LockAccount(Guid id, [FromBody] LockAccountRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new LockAccountCommand(
            id,
            request.Reason,
            request.LockDurationMinutes,
            userId);

        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Unlock a user account.
    /// </summary>
    [HttpPost("{id:guid}/unlock")]
    [RequirePermission("users:manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UnlockAccount(Guid id)
    {
        var userId = GetCurrentUserId();
        var command = new UnlockAccountCommand(id, userId);

        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Activate a user account.
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    [RequirePermission("users:manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ActivateAccount(Guid id)
    {
        var userId = GetCurrentUserId();
        var command = new ActivateAccountCommand(id, userId);

        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Deactivate a user account.
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission("users:manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeactivateAccount(Guid id)
    {
        var userId = GetCurrentUserId();
        var command = new DeactivateAccountCommand(id, userId);

        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Get the current authenticated user's profile.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyProfile()
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var query = new GetUserByIdQuery(userId);
        var result = await _mediator.Send(query);

        return result.Match(
            user => Ok(user),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Update the current authenticated user's profile.
    /// This is a self-service endpoint that doesn't require admin permissions.
    /// </summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var command = new UpdateProfileCommand(
            userId,
            request.FirstName,
            request.LastName,
            request.DisplayName,
            request.PhoneNumber,
            request.PreferredLanguage,
            request.TimeZone);

        var result = await _mediator.Send(command);

        return result.Match(
            user => Ok(user),
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
public record CreateUserRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? DisplayName = null,
    string? PhoneNumber = null,
    string? PreferredLanguage = null,
    string? TimeZone = null,
    IReadOnlyList<Guid>? RoleIds = null);

public record UpdateUserRequest(
    string FirstName,
    string LastName,
    string? DisplayName = null,
    string? PhoneNumber = null,
    string? PreferredLanguage = null,
    string? TimeZone = null);

public record AssignRoleRequest(
    Guid RoleId,
    DateTime? ExpiresAt = null);

public record UpdateProfileRequest(
    string? FirstName = null,
    string? LastName = null,
    string? DisplayName = null,
    string? PhoneNumber = null,
    string? PreferredLanguage = null,
    string? TimeZone = null);

public record LockAccountRequest(
    string Reason,
    int? LockDurationMinutes = null);

public record GrantPermissionRequest(
    Guid PermissionId,
    Guid? ApplicationId = null,
    DateTime? ExpiresAt = null);
