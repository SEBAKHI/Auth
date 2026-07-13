using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.OrganizationManagement.Contracts;
using Auth.Application.Features.Organizations.AssignAppRole;
using Auth.Application.Features.Organizations.CreateOrganization;
using Auth.Application.Features.Organizations.DeleteOrganization;
using Auth.Application.Features.Organizations.DisableApplication;
using Auth.Application.Features.Organizations.EnableApplication;
using Auth.Application.Features.Organizations.GetAllOrganizations;
using Auth.Application.Features.Organizations.GetMemberAppRoles;
using Auth.Application.Features.Organizations.GetOrganizationApplications;
using Auth.Application.Features.Organizations.GetOrganizationById;
using Auth.Application.Features.Organizations.GetOrganizationMembers;
using Auth.Application.Features.Organizations.GetPendingInvitations;
using Auth.Application.Features.Organizations.GetUserOrganizations;
using Auth.Application.Features.Organizations.GrantPermission;
using Auth.Application.Features.Organizations.InviteMember;
using Auth.Application.Features.Organizations.RemoveAppRole;
using Auth.Application.Features.Organizations.ResendInvitation;
using Auth.Application.Features.Organizations.RemoveMember;
using Auth.Application.Features.Organizations.UpdateMemberRole;
using Auth.Application.Features.Organizations.UpdateOrganization;
using Auth.Application.Features.Organizations.UpdateOrganizationApplication;
using Auth.Application.DTOs;
using Auth.Domain.Enums;
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
public class OrganizationsController : ApiController
{
    private readonly ISender _sender;

    public OrganizationsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Get all organizations the current user is a member of.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<OrganizationSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyOrganizations(
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var query = new GetUserOrganizationsQuery(userId, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            organizations => Ok(organizations),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get a paginated list of ALL organizations (platform administration).
    /// </summary>
    [HttpGet("all")]
    [RequirePermission("organizations:read")]
    [ProducesResponseType(typeof(PagedOrganizationsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllOrganizations(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetAllOrganizationsQuery(
            pageNumber, pageSize, searchTerm, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            organizations => Ok(organizations),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get organization details by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrganizationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOrganization(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var query = new GetOrganizationByIdQuery(id)
        {
            RequestedBy = userId,
            PlatformScope = HasPermissionClaim("organizations:read")
        };
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            organization => Ok(organization),
            errors => Problem(errors));
    }

    /// <summary>
    /// Create a new organization. The creating user becomes the owner.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrganizationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateOrganization([FromBody] CreateOrganizationRequest request, CancellationToken cancellationToken)
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

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            organization => CreatedAtAction(nameof(GetOrganization), new { id = organization.Id }, organization),
            errors => Problem(errors));
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
    public async Task<IActionResult> UpdateOrganization(Guid id, [FromBody] UpdateOrganizationRequest request, CancellationToken cancellationToken)
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

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            organization => Ok(organization),
            errors => Problem(errors));
    }

    /// <summary>
    /// Delete an organization. Only the owner can delete.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteOrganization(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new DeleteOrganizationCommand(id)
        {
            RequestedBy = userId,
            PlatformScope = HasPermissionClaim("organizations:manage")
        };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
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
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var query = new GetOrganizationMembersQuery(id, pageNumber, pageSize, search, sortBy, sortDirection)
        {
            RequestedBy = userId,
            PlatformScope = HasPermissionClaim("organizations:read")
        };
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            members => Ok(members),
            errors => Problem(errors));
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
        [FromBody] UpdateMemberRoleRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var command = new UpdateMemberRoleCommand(orgId, userId, request.RoleId) { ModifiedBy = currentUserId };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            member => Ok(member),
            errors => Problem(errors));
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
    public async Task<IActionResult> RemoveMember(Guid orgId, Guid userId, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var command = new RemoveMemberCommand(orgId, userId) { RemovedBy = currentUserId };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
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
    public async Task<IActionResult> GetPendingInvitations(
        Guid id,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var query = new GetPendingInvitationsQuery(id, sortBy, sortDirection)
        {
            RequestedBy = userId,
            PlatformScope = HasPermissionClaim("organizations:read")
        };
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            invitations => Ok(invitations),
            errors => Problem(errors));
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
    public async Task<IActionResult> InviteMember(Guid id, [FromBody] InviteMemberRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new InviteMemberCommand(id, request.Email, request.RoleId) { InvitedBy = userId };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            invitation => CreatedAtAction(nameof(GetPendingInvitations), new { id }, invitation),
            errors => Problem(errors));
    }

    /// <summary>
    /// Resend an organization invitation with a new token.
    /// </summary>
    [HttpPost("{orgId:guid}/invitations/{invitationId:guid}/resend")]
    [RequirePermission("org:members:invite")]
    [ProducesResponseType(typeof(OrganizationInvitationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResendInvitation(Guid orgId, Guid invitationId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new ResendInvitationCommand(orgId, invitationId) { ResentBy = userId };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            invitation => Ok(invitation),
            errors => Problem(errors));
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
    public async Task<IActionResult> GetApplications(
        Guid id,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var query = new GetOrganizationApplicationsQuery(id, sortBy, sortDirection)
        {
            RequestedBy = userId,
            PlatformScope = HasPermissionClaim("organizations:read")
        };
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            apps => Ok(apps),
            errors => Problem(errors));
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
    public async Task<IActionResult> EnableApplication(Guid id, [FromBody] EnableApplicationRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new EnableApplicationCommand(id, request.ApplicationId, request.SubscriptionTier, request.ExpiresAt)
        {
            EnabledBy = userId
        };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            app => CreatedAtAction(nameof(GetApplications), new { id }, app),
            errors => Problem(errors));
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
    public async Task<IActionResult> UpdateApplication(Guid id, Guid applicationId, [FromBody] UpdateOrganizationApplicationRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new UpdateOrganizationApplicationCommand(id, applicationId, request.SubscriptionTier, request.ExpiresAt, request.IsActive)
        {
            ModifiedBy = userId
        };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            app => Ok(app),
            errors => Problem(errors));
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
    public async Task<IActionResult> DisableApplication(Guid id, Guid applicationId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new DisableApplicationCommand(id, applicationId) { DisabledBy = userId };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    #endregion

    #region Member App Roles

    /// <summary>
    /// Get all app-level role assignments for a member.
    /// </summary>
    [HttpGet("{orgId:guid}/members/{userId:guid}/roles")]
    [RequirePermission("org:permissions:read")]
    [ProducesResponseType(typeof(IReadOnlyList<OrganizationMemberAppRoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMemberAppRoles(
        Guid orgId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var query = new GetMemberAppRolesQuery(orgId, userId) { RequestedBy = currentUserId };
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            roles => Ok(roles),
            errors => Problem(errors));
    }

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
        [FromBody] AssignAppRoleRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var command = new AssignAppRoleCommand(orgId, userId, request.ApplicationId, request.RoleId, request.ExpiresAt)
        {
            AssignedBy = currentUserId
        };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            role => CreatedAtAction(nameof(GetMembers), new { id = orgId }, role),
            errors => Problem(errors));
    }

    /// <summary>
    /// Remove an app-level role from a member. The application is derived
    /// from the role, since a role belongs to exactly one application.
    /// </summary>
    [HttpDelete("{orgId:guid}/members/{userId:guid}/roles/{roleId:guid}")]
    [RequirePermission("org:permissions:manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveAppRole(
        Guid orgId,
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var command = new RemoveAppRoleCommand(orgId, userId, roleId) { RemovedBy = currentUserId };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
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
        [FromBody] GrantPermissionRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var command = new GrantPermissionCommand(orgId, userId, request.ApplicationId, request.PermissionId, request.ExpiresAt)
        {
            GrantedBy = currentUserId
        };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            permission => CreatedAtAction(nameof(GetMembers), new { id = orgId }, permission),
            errors => Problem(errors));
    }

    #endregion

}
