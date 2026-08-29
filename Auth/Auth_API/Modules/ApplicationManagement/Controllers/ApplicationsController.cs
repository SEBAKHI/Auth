using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.ApplicationManagement.Contracts;
using Auth.Application.Features.Applications.CreateApplication;
using Auth.Application.Features.Applications.DeleteApplication;
using Auth.Application.Features.Applications.GetApplicationAccessGrants;
using Auth.Application.Features.Applications.GetApplicationById;
using Auth.Application.Features.Applications.GrantApplicationAccess;
using Auth.Application.Features.Applications.RevokeApplicationAccess;
using Auth.Application.Features.Applications.SetApplicationActive;
using Auth.Application.Features.Applications.GetApplicationOrganizations;
using Auth.Application.Features.Applications.GetApplicationPermissions;
using Auth.Application.Features.Applications.GetApplicationRoles;
using Auth.Application.Features.Applications.GetApplications;
using Auth.Application.Features.Applications.GetApplicationUsers;
using Auth.Application.Features.Applications.GetPublicBranding;
using Auth.Application.Features.Applications.UpdateApplication;
using Auth.Application.DTOs;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.ApplicationManagement.Controllers;

/// <summary>
/// Controller for application management operations.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ApplicationsController : ApiController
{
    private readonly ISender _sender;

    public ApplicationsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Get a paginated list of applications.
    /// </summary>
    [HttpGet]
    [RequirePermission(PermissionCodes.Applications.Read)]
    [ProducesResponseType(typeof(PagedApplicationsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApplications(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? searchTerm = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetApplicationsQuery(pageNumber, pageSize, searchTerm, isActive, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            applications => Ok(applications),
            errors => Problem(errors));
    }

    /// <summary>
    /// Public branding for the hosted login page: display name and logo only.
    /// Anonymous by design — the accounts app calls it during an authorize
    /// flow; unknown and inactive applications are both 404.
    /// </summary>
    [HttpGet("{clientId}/public-branding")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublicBrandingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublicBranding(string clientId, CancellationToken cancellationToken)
    {
        var query = new GetPublicBrandingQuery(clientId);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            branding => Ok(branding),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get an application by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.Applications.Read)]
    [ProducesResponseType(typeof(ApplicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApplication(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetApplicationByIdQuery(id);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            application => Ok(application),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get all roles for an application.
    /// </summary>
    [HttpGet("{id:guid}/roles")]
    [RequirePermission(PermissionCodes.Applications.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApplicationRoles(
        Guid id,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetApplicationRolesQuery(id, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            roles => Ok(roles),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get all permissions for an application.
    /// </summary>
    [HttpGet("{id:guid}/permissions")]
    [RequirePermission(PermissionCodes.Applications.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<PermissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApplicationPermissions(
        Guid id,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetApplicationPermissionsQuery(id, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            permissions => Ok(permissions),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get paginated users under an application.
    /// </summary>
    [HttpGet("{id:guid}/users")]
    [RequirePermission(PermissionCodes.Applications.Read)]
    [ProducesResponseType(typeof(PagedApplicationUsersDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApplicationUsers(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetApplicationUsersQuery(id, pageNumber, pageSize, searchTerm, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            users => Ok(users),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get paginated organizations that have an application enabled.
    /// </summary>
    [HttpGet("{id:guid}/organizations")]
    [RequirePermission(PermissionCodes.Applications.Read)]
    [ProducesResponseType(typeof(PagedApplicationOrganizationsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApplicationOrganizations(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetApplicationOrganizationsQuery(id, pageNumber, pageSize, searchTerm, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            organizations => Ok(organizations),
            errors => Problem(errors));
    }

    /// <summary>
    /// Create a new application.
    /// </summary>
    [HttpPost]
    [RequirePermission(PermissionCodes.Applications.Create)]
    [ProducesResponseType(typeof(ApplicationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateApplication([FromBody] CreateApplicationRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new CreateApplicationCommand(
            request.Code,
            request.Name,
            request.Description,
            request.BaseUrl,
            request.LogoUrl,
            request.ContactEmail,
            request.AllowSelfRegistration,
            request.RequireTwoFactor,
            request.RequireEmailVerification,
            request.SessionTimeoutMinutes,
            request.MaxConcurrentSessions,
            request.RedirectUris,
            request.ReauthenticationMaxAgeMinutes,
            request.AccessMode)
        {
            CreatedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            application => CreatedAtAction(nameof(GetApplication), new { id = application.Id }, application),
            errors => Problem(errors));
    }

    /// <summary>
    /// Update an existing application.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.Applications.Update)]
    [ProducesResponseType(typeof(ApplicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateApplication(Guid id, [FromBody] UpdateApplicationRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new UpdateApplicationCommand(
            id,
            request.Name,
            request.Description,
            request.BaseUrl,
            request.LogoUrl,
            request.ContactEmail,
            request.AllowSelfRegistration,
            request.RequireTwoFactor,
            request.RequireEmailVerification,
            request.SessionTimeoutMinutes,
            request.MaxConcurrentSessions,
            request.RedirectUris,
            request.ReauthenticationMaxAgeMinutes,
            request.AccessMode)
        {
            ModifiedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            application => Ok(application),
            errors => Problem(errors));
    }

    /// <summary>
    /// Delete an application.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.Applications.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteApplication(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new DeleteApplicationCommand(id) { DeletedBy = userId };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Switch an application on. Its access mode is left as it was.
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    [RequirePermission(PermissionCodes.Applications.Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ActivateApplication(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SetApplicationActiveCommand(id, true) { ModifiedBy = GetCurrentUserId() },
            cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Switch an application off. Nobody signs in and no token refreshes while
    /// it is off, whatever its access mode; its refresh tokens and sessions are
    /// revoked immediately.
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission(PermissionCodes.Applications.Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeactivateApplication(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SetApplicationActiveCommand(id, false) { ModifiedBy = GetCurrentUserId() },
            cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get the application's access list — the users individually invited to it.
    /// </summary>
    [HttpGet("{id:guid}/access")]
    [RequirePermission(PermissionCodes.Applications.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<ApplicationAccessGrantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApplicationAccess(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetApplicationAccessGrantsQuery(id), cancellationToken);

        return result.Match(
            grants => Ok(grants),
            errors => Problem(errors));
    }

    /// <summary>
    /// Invite a user to the application, optionally with a role scoped to it.
    /// </summary>
    [HttpPost("{id:guid}/access")]
    [RequirePermission(PermissionCodes.Applications.Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GrantApplicationAccess(
        Guid id,
        [FromBody] GrantApplicationAccessRequest request,
        CancellationToken cancellationToken)
    {
        var command = new GrantApplicationAccessCommand(
            id, request.UserId, request.RoleId, request.ExpiresAt, request.Note)
        {
            GrantedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Withdraw a user's invitation. Their tokens and sessions for this
    /// application are revoked; other applications are untouched.
    /// </summary>
    [HttpDelete("{id:guid}/access/{userId:guid}")]
    [RequirePermission(PermissionCodes.Applications.Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevokeApplicationAccess(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var command = new RevokeApplicationAccessCommand(id, userId)
        {
            RevokedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }
}
