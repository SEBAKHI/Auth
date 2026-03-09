using Asp.Versioning;
using Auth_API.Authorization;
using Auth_Lib.Application.Features.Applications.CreateApplication;
using Auth_Lib.Application.Features.Applications.DeleteApplication;
using Auth_Lib.Application.Features.Applications.GetApplicationById;
using Auth_Lib.Application.Features.Applications.GetApplicationPermissions;
using Auth_Lib.Application.Features.Applications.GetApplicationRoles;
using Auth_Lib.Application.Features.Applications.GetApplications;
using Auth_Lib.Application.Features.Applications.UpdateApplication;
using Auth_Lib.Application.DTOs;
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
public class ApplicationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ApplicationsController(IMediator mediator)
    {
        _mediator = mediator;
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
        [FromQuery] bool? isActive = null)
    {
        var query = new GetApplicationsQuery(pageNumber, pageSize, search, isActive);
        var result = await _mediator.Send(query);

        return result.Match(
            applications => Ok(applications),
            errors => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: errors.First().Description));
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
    public async Task<IActionResult> GetApplication(Guid id)
    {
        var query = new GetApplicationByIdQuery(id);
        var result = await _mediator.Send(query);

        return result.Match(
            application => Ok(application),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
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
    public async Task<IActionResult> GetApplicationRoles(Guid id)
    {
        var query = new GetApplicationRolesQuery(id);
        var result = await _mediator.Send(query);

        return result.Match(
            roles => Ok(roles),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
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
    public async Task<IActionResult> GetApplicationPermissions(Guid id)
    {
        var query = new GetApplicationPermissionsQuery(id);
        var result = await _mediator.Send(query);

        return result.Match(
            permissions => Ok(permissions),
            errors => errors.First().Type == ErrorOr.ErrorType.NotFound
                ? NotFound(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
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
    public async Task<IActionResult> CreateApplication([FromBody] CreateApplicationRequest request)
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
            request.MaxConcurrentSessions)
        {
            CreatedBy = userId
        };

        var result = await _mediator.Send(command);

        return result.Match(
            application => CreatedAtAction(nameof(GetApplication), new { id = application.Id }, application),
            errors => errors.First().Type == ErrorOr.ErrorType.Conflict
                ? Conflict(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
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
    public async Task<IActionResult> UpdateApplication(Guid id, [FromBody] UpdateApplicationRequest request)
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
            request.MaxConcurrentSessions)
        {
            ModifiedBy = userId
        };

        var result = await _mediator.Send(command);

        return result.Match(
            application => Ok(application),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
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
    public async Task<IActionResult> DeleteApplication(Guid id)
    {
        var userId = GetCurrentUserId();
        var command = new DeleteApplicationCommand(id) { DeletedBy = userId };
        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                ErrorOr.ErrorType.Conflict => Conflict(new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}

// Request DTOs
public record CreateApplicationRequest(
    string Code,
    string Name,
    string? Description = null,
    string? BaseUrl = null,
    string? LogoUrl = null,
    string? ContactEmail = null,
    bool AllowSelfRegistration = false,
    bool RequireTwoFactor = false,
    bool RequireEmailVerification = false,
    int SessionTimeoutMinutes = 60,
    int MaxConcurrentSessions = 5);

public record UpdateApplicationRequest(
    string Name,
    string? Description = null,
    string? BaseUrl = null,
    string? LogoUrl = null,
    string? ContactEmail = null,
    bool AllowSelfRegistration = false,
    bool RequireTwoFactor = false,
    bool RequireEmailVerification = false,
    int SessionTimeoutMinutes = 60,
    int MaxConcurrentSessions = 5);
