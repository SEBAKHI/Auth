using Asp.Versioning;
using Auth_API.Authorization;
using Auth_Lib.Application.Features.Organizations.AssignAppRole;
using Auth_Lib.Application.Features.Organizations.CreateOrganization;
using Auth_Lib.Application.Features.Organizations.DeleteOrganization;
using Auth_Lib.Application.Features.Organizations.DisableApplication;
using Auth_Lib.Application.Features.Organizations.EnableApplication;
using Auth_Lib.Application.Features.Organizations.GetOrganizationApplications;
using Auth_Lib.Application.Features.Organizations.GetOrganizationById;
using Auth_Lib.Application.Features.Organizations.GetOrganizationMembers;
using Auth_Lib.Application.Features.Organizations.GetPendingInvitations;
using Auth_Lib.Application.Features.Organizations.GetUserOrganizations;
using Auth_Lib.Application.Features.Organizations.GrantPermission;
using Auth_Lib.Application.Features.Organizations.InviteMember;
using Auth_Lib.Application.Features.Organizations.RemoveMember;
using Auth_Lib.Application.Features.Organizations.UpdateMemberRole;
using Auth_Lib.Application.Features.Organizations.UpdateOrganization;
using Auth_Lib.Application.Features.Organizations.UpdateOrganizationApplication;
using Auth_Lib.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.OrganizationManagement.Controllers;

/// <summary>
/// Controller for organization management operations.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class OrganizationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrganizationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all organizations the current user is a member of.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<OrganizationSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyOrganizations()
    {
        var userId = GetCurrentUserId();
        var query = new GetUserOrganizationsQuery(userId);
        var result = await _mediator.Send(query);

        return result.Match(
            organizations => Ok(organizations),
            errors => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: errors.First().Description));
    }

    /// <summary>
    /// Get organization details by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrganizationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOrganization(Guid id)
    {
        var userId = GetCurrentUserId();
        var query = new GetOrganizationByIdQuery(id) { RequestedBy = userId };
        var result = await _mediator.Send(query);

        return result.Match(
            organization => Ok(organization),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    /// <summary>
    /// Create a new organization. The creating user becomes the owner.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrganizationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateOrganization([FromBody] CreateOrganizationRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new CreateOrganizationCommand(
            request.Code,
            request.Name,
            request.ContactEmail,
            request.Description,
            request.LogoUrl,
            request.Website)
        {
            CreatedBy = userId
        };

        var result = await _mediator.Send(command);

        return result.Match(
            organization => CreatedAtAction(nameof(GetOrganization), new { id = organization.Id }, organization),
            errors => errors.First().Type == ErrorOr.ErrorType.Conflict
                ? Conflict(new { error = errors.First().Description })
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Update an organization.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("org:update")]
    [ProducesResponseType(typeof(OrganizationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateOrganization(Guid id, [FromBody] UpdateOrganizationRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new UpdateOrganizationCommand(
            id,
            request.Name,
            request.ContactEmail,
            request.Description,
            request.LogoUrl,
            request.Website,
            request.IsActive)
        {
            ModifiedBy = userId
        };

        var result = await _mediator.Send(command);

        return result.Match(
            organization => Ok(organization),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    /// <summary>
    /// Delete an organization. Only the owner can delete.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteOrganization(Guid id)
    {
        var userId = GetCurrentUserId();
        var command = new DeleteOrganizationCommand(id) { RequestedBy = userId };
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

    #region Members

    /// <summary>
    /// Get paginated members of an organization.
    /// </summary>
    [HttpGet("{id:guid}/members")]
    [RequirePermission("org:members:read")]
    [ProducesResponseType(typeof(PagedOrganizationMembersDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMembers(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        var userId = GetCurrentUserId();
        var query = new GetOrganizationMembersQuery(id, pageNumber, pageSize, search) { RequestedBy = userId };
        var result = await _mediator.Send(query);

        return result.Match(
            members => Ok(members),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    /// <summary>
    /// Update a member's organization role.
    /// </summary>
    [HttpPut("{orgId:guid}/members/{userId:guid}/role")]
    [RequirePermission("org:members:manage")]
    [ProducesResponseType(typeof(OrganizationMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateMemberRole(
        Guid orgId,
        Guid userId,
        [FromBody] UpdateMemberRoleRequest request)
    {
        var currentUserId = GetCurrentUserId();
        var command = new UpdateMemberRoleCommand(orgId, userId, request.RoleId) { ModifiedBy = currentUserId };
        var result = await _mediator.Send(command);

        return result.Match(
            member => Ok(member),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    /// <summary>
    /// Remove a member from an organization.
    /// </summary>
    [HttpDelete("{orgId:guid}/members/{userId:guid}")]
    [RequirePermission("org:members:manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveMember(Guid orgId, Guid userId)
    {
        var currentUserId = GetCurrentUserId();
        var command = new RemoveMemberCommand(orgId, userId) { RemovedBy = currentUserId };
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

    #endregion

    #region Invitations

    /// <summary>
    /// Get pending invitations for an organization.
    /// </summary>
    [HttpGet("{id:guid}/invitations")]
    [RequirePermission("org:members:read")]
    [ProducesResponseType(typeof(IReadOnlyList<OrganizationInvitationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPendingInvitations(Guid id)
    {
        var userId = GetCurrentUserId();
        var query = new GetPendingInvitationsQuery(id) { RequestedBy = userId };
        var result = await _mediator.Send(query);

        return result.Match(
            invitations => Ok(invitations),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    /// <summary>
    /// Invite a user to an organization.
    /// </summary>
    [HttpPost("{id:guid}/invitations")]
    [RequirePermission("org:members:invite")]
    [ProducesResponseType(typeof(OrganizationInvitationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> InviteMember(Guid id, [FromBody] InviteMemberRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new InviteMemberCommand(id, request.Email, request.RoleId) { InvitedBy = userId };
        var result = await _mediator.Send(command);

        return result.Match(
            invitation => CreatedAtAction(nameof(GetPendingInvitations), new { id }, invitation),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Conflict => Conflict(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    #endregion

    #region Applications

    /// <summary>
    /// Get all enabled applications for an organization.
    /// </summary>
    [HttpGet("{id:guid}/applications")]
    [RequirePermission("org:apps:read")]
    [ProducesResponseType(typeof(IReadOnlyList<OrganizationApplicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApplications(Guid id)
    {
        var userId = GetCurrentUserId();
        var query = new GetOrganizationApplicationsQuery(id) { RequestedBy = userId };
        var result = await _mediator.Send(query);

        return result.Match(
            apps => Ok(apps),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    /// <summary>
    /// Enable an application for an organization.
    /// </summary>
    [HttpPost("{id:guid}/applications")]
    [RequirePermission("org:apps:manage")]
    [ProducesResponseType(typeof(OrganizationApplicationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EnableApplication(Guid id, [FromBody] EnableApplicationRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new EnableApplicationCommand(id, request.ApplicationId, request.SubscriptionTier, request.ExpiresAt)
        {
            EnabledBy = userId
        };
        var result = await _mediator.Send(command);

        return result.Match(
            app => CreatedAtAction(nameof(GetApplications), new { id }, app),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Conflict => Conflict(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    /// <summary>
    /// Update an application subscription for an organization.
    /// </summary>
    [HttpPut("{id:guid}/applications/{applicationId:guid}")]
    [RequirePermission("org:apps:manage")]
    [ProducesResponseType(typeof(OrganizationApplicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateApplication(Guid id, Guid applicationId, [FromBody] UpdateApplicationRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new UpdateOrganizationApplicationCommand(id, applicationId, request.SubscriptionTier, request.ExpiresAt, request.IsActive)
        {
            ModifiedBy = userId
        };
        var result = await _mediator.Send(command);

        return result.Match(
            app => Ok(app),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    /// <summary>
    /// Disable an application for an organization.
    /// </summary>
    [HttpDelete("{id:guid}/applications/{applicationId:guid}")]
    [RequirePermission("org:apps:manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DisableApplication(Guid id, Guid applicationId)
    {
        var userId = GetCurrentUserId();
        var command = new DisableApplicationCommand(id, applicationId) { DisabledBy = userId };
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

    #endregion

    #region Member App Roles

    /// <summary>
    /// Assign an app-level role to a member.
    /// </summary>
    [HttpPost("{orgId:guid}/members/{userId:guid}/roles")]
    [RequirePermission("org:permissions:manage")]
    [ProducesResponseType(typeof(OrganizationMemberAppRoleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignAppRole(
        Guid orgId,
        Guid userId,
        [FromBody] AssignAppRoleRequest request)
    {
        var currentUserId = GetCurrentUserId();
        var command = new AssignAppRoleCommand(orgId, userId, request.ApplicationId, request.RoleId, request.ExpiresAt)
        {
            AssignedBy = currentUserId
        };
        var result = await _mediator.Send(command);

        return result.Match(
            role => CreatedAtAction(nameof(GetMembers), new { id = orgId }, role),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Conflict => Conflict(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    #endregion

    #region Member Permissions

    /// <summary>
    /// Grant an individual permission to a member.
    /// </summary>
    [HttpPost("{orgId:guid}/members/{userId:guid}/permissions")]
    [RequirePermission("org:permissions:manage")]
    [ProducesResponseType(typeof(OrganizationMemberPermissionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GrantPermission(
        Guid orgId,
        Guid userId,
        [FromBody] GrantPermissionRequest request)
    {
        var currentUserId = GetCurrentUserId();
        var command = new GrantPermissionCommand(orgId, userId, request.ApplicationId, request.PermissionId, request.ExpiresAt)
        {
            GrantedBy = currentUserId
        };
        var result = await _mediator.Send(command);

        return result.Match(
            permission => CreatedAtAction(nameof(GetMembers), new { id = orgId }, permission),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Conflict => Conflict(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    #endregion

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}

#region Request DTOs

public record CreateOrganizationRequest(
    string Code,
    string Name,
    string ContactEmail,
    string? Description = null,
    string? LogoUrl = null,
    string? Website = null);

public record UpdateOrganizationRequest(
    string Name,
    string ContactEmail,
    string? Description = null,
    string? LogoUrl = null,
    string? Website = null,
    bool? IsActive = null);

public record UpdateMemberRoleRequest(Guid RoleId);

public record InviteMemberRequest(string Email, Guid RoleId);

public record EnableApplicationRequest(
    Guid ApplicationId,
    string? SubscriptionTier = null,
    DateTime? ExpiresAt = null);

public record UpdateApplicationRequest(
    string? SubscriptionTier = null,
    DateTime? ExpiresAt = null,
    bool? IsActive = null);

public record AssignAppRoleRequest(
    Guid ApplicationId,
    Guid RoleId,
    DateTime? ExpiresAt = null);

public record GrantPermissionRequest(
    Guid ApplicationId,
    Guid PermissionId,
    DateTime? ExpiresAt = null);

#endregion
