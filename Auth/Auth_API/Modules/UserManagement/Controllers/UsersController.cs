using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.UserManagement.Contracts;
using Auth.Application.Features.Users.ActivateAccount;
using Auth.Application.Features.Users.AssignRole;
using Auth.Application.Features.Users.CreateUser;
using Auth.Application.Features.Users.DeactivateAccount;
using Auth.Application.Features.Users.DeleteUser;
using Auth.Application.Features.Organizations.GetUserOrganizations;
using Auth.Application.Features.Users.GetUserApplications;
using Auth.Application.Features.Users.GetUserById;
using Auth.Application.Features.Users.GetUserPermissions;
using Auth.Application.Features.Users.GetUserRoles;
using Auth.Application.Features.Users.GetUsers;
using Auth.Application.Features.Users.GrantUserPermission;
using Auth.Application.Features.Users.LockAccount;
using Auth.Application.Features.Users.RemoveProfileImage;
using Auth.Application.Features.Users.RemoveUserRole;
using Auth.Application.Features.Users.RevokeUserPermission;
using Auth.Application.Features.Users.SetProfileImage;
using Auth.Application.Features.Users.UnlockAccount;
using Auth.Application.Features.Users.UpdateProfile;
using Auth.Application.Features.Users.UpdateUser;
using Auth.Application.DTOs;
using Auth.Domain.Enums;
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
public class UsersController : ApiController
{
    private readonly ISender _sender;

    public UsersController(ISender sender)
    {
        _sender = sender;
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
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetUsersQuery(pageNumber, pageSize, searchTerm, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            users => Ok(users),
            errors => Problem(errors));
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
    public async Task<IActionResult> GetUser(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetUserByIdQuery(id);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            user => Ok(user),
            errors => Problem(errors));
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
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
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
            request.Theme,
            request.RoleIds)
        {
            CreatedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            user => CreatedAtAction(nameof(GetUser), new { id = user.Id }, user),
            errors => Problem(errors));
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
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new UpdateUserCommand(
            id,
            request.FirstName,
            request.LastName,
            request.DisplayName,
            request.PhoneNumber,
            request.PreferredLanguage,
            request.TimeZone,
            request.Theme)
        {
            ModifiedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            user => Ok(user),
            errors => Problem(errors));
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
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new DeleteUserCommand(id) { DeletedBy = userId };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
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
    public async Task<IActionResult> AssignRole(Guid id, [FromBody] AssignRoleRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new AssignRoleCommand(id, request.RoleId, request.ExpiresAt)
        {
            AssignedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
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
    public async Task<IActionResult> GetUserRoles(
        Guid id,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetUserRolesQuery(id, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            roles => Ok(roles),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get all organizations a user is a member of.
    /// </summary>
    [HttpGet("{id:guid}/organizations")]
    [RequirePermission("users:read")]
    [ProducesResponseType(typeof(IReadOnlyList<OrganizationSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUserOrganizations(
        Guid id,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetUserOrganizationsQuery(id, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            organizations => Ok(organizations),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get all applications a user has access to.
    /// </summary>
    [HttpGet("{id:guid}/applications")]
    [RequirePermission("users:read")]
    [ProducesResponseType(typeof(IReadOnlyList<UserApplicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUserApplications(
        Guid id,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetUserApplicationsQuery(id, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            applications => Ok(applications),
            errors => Problem(errors));
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
    public async Task<IActionResult> RemoveRole(Guid id, Guid roleId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new RemoveUserRoleCommand(id, roleId) { RemovedBy = userId };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
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
    public async Task<IActionResult> GetUserPermissions(
        Guid id,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetUserPermissionsQuery(id, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            permissions => Ok(permissions),
            errors => Problem(errors));
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
    public async Task<IActionResult> GrantPermission(Guid id, [FromBody] GrantPermissionRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new GrantUserPermissionCommand(id, request.PermissionId, request.ApplicationId, request.ExpiresAt)
        {
            GrantedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
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
    public async Task<IActionResult> RevokePermission(Guid id, Guid permissionId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new RevokeUserPermissionCommand(id, permissionId) { RevokedBy = userId };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
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
    public async Task<IActionResult> LockAccount(Guid id, [FromBody] LockAccountRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new LockAccountCommand(
            id,
            request.Reason,
            request.LockDurationMinutes,
            userId);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
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
    public async Task<IActionResult> UnlockAccount(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new UnlockAccountCommand(id, userId);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
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
    public async Task<IActionResult> ActivateAccount(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new ActivateAccountCommand(id, userId);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
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
    public async Task<IActionResult> DeactivateAccount(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new DeactivateAccountCommand(id, userId);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get the current authenticated user's profile.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var query = new GetUserByIdQuery(userId);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            user => Ok(user),
            errors => Problem(errors));
    }

    /// <summary>
    /// Update the current authenticated user's profile.
    /// This is a self-service endpoint that doesn't require admin permissions.
    /// </summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
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
            request.TimeZone,
            request.Theme);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            user => Ok(user),
            errors => Problem(errors));
    }

    /// <summary>Sets the current user's profile image to a previously uploaded image key.</summary>
    [HttpPut("me/profile-image")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SetMyProfileImage(
        [FromBody] SetProfileImageRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new SetProfileImageCommand(userId, request.ImageKey, userId), cancellationToken);

        return result.Match(_ => NoContent(), errors => Problem(errors));
    }

    /// <summary>Clears the current user's profile image.</summary>
    [HttpDelete("me/profile-image")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemoveMyProfileImage(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new RemoveProfileImageCommand(userId, userId), cancellationToken);

        return result.Match(_ => NoContent(), errors => Problem(errors));
    }

    /// <summary>Sets a user's profile image (admin).</summary>
    [HttpPut("{id:guid}/profile-image")]
    [RequirePermission("users:update")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetUserProfileImage(
        Guid id, [FromBody] SetProfileImageRequest request, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SetProfileImageCommand(id, request.ImageKey, GetCurrentUserId()), cancellationToken);

        return result.Match(_ => NoContent(), errors => Problem(errors));
    }

    /// <summary>Clears a user's profile image (admin).</summary>
    [HttpDelete("{id:guid}/profile-image")]
    [RequirePermission("users:update")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveUserProfileImage(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new RemoveProfileImageCommand(id, GetCurrentUserId()), cancellationToken);

        return result.Match(_ => NoContent(), errors => Problem(errors));
    }

}
