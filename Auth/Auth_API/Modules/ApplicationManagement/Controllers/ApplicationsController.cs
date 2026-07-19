using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.ApplicationManagement.Contracts;
using Auth.Application.Features.Applications.CreateApplication;
using Auth.Application.Features.Applications.DeleteApplication;
using Auth.Application.Features.Applications.GetApplicationById;
using Auth.Application.Features.Applications.GetApplicationOrganizations;
using Auth.Application.Features.Applications.GetApplicationPermissions;
using Auth.Application.Features.Applications.GetApplicationRoles;
using Auth.Application.Features.Applications.GetApplications;
using Auth.Application.Features.Applications.GetApplicationUsers;
using Auth.Application.Features.Applications.GetPublicBranding;
using Auth.Application.Features.Applications.UpdateApplication;
using Auth.Application.DTOs;
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
    [RequirePermission("applications:read")]
    [ProducesResponseType(typeof(PagedApplicationsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApplications(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetApplicationsQuery(pageNumber, pageSize, search, isActive, sortBy, sortDirection);
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
    [RequirePermission("applications:read")]
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
    [RequirePermission("applications:read")]
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
    [RequirePermission("applications:read")]
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
    [RequirePermission("applications:read")]
    [ProducesResponseType(typeof(PagedApplicationUsersDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApplicationUsers(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetApplicationUsersQuery(id, pageNumber, pageSize, search, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            users => Ok(users),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get paginated organizations that have an application enabled.
    /// </summary>
    [HttpGet("{id:guid}/organizations")]
    [RequirePermission("applications:read")]
    [ProducesResponseType(typeof(PagedApplicationOrganizationsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApplicationOrganizations(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetApplicationOrganizationsQuery(id, pageNumber, pageSize, search, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            organizations => Ok(organizations),
            errors => Problem(errors));
    }

    /// <summary>
    /// Create a new application.
    /// </summary>
    [HttpPost]
    [RequirePermission("applications:create")]
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
            request.ReauthenticationMaxAgeMinutes)
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
    [RequirePermission("applications:update")]
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
            request.ReauthenticationMaxAgeMinutes)
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
    [RequirePermission("applications:delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteApplication(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new DeleteApplicationCommand(id) { DeletedBy = userId };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

}
