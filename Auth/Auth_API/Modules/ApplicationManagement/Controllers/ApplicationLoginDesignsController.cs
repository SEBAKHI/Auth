using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Modules.ApplicationManagement.Contracts;
using Auth.Application.DTOs;
using Auth.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.ApplicationManagement.Controllers;

/// <summary>
/// An application's sign-in page designs (the console's "Templates" tab): the HTML and CSS its
/// sign-in pages show in their panel half. Any number of designs, at most one active; a new
/// design and a copy start inactive, and saving the active one is live. No new permission:
/// reading needs <c>applications:read</c>, every change <c>applications:update</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/applications/{applicationId:guid}/login-designs")]
[Authorize]
public class ApplicationLoginDesignsController : LoginDesignEndpoints
{
    public ApplicationLoginDesignsController(ISender sender) : base(sender)
    {
    }

    /// <summary>The application's designs, the active one first.</summary>
    [HttpGet]
    [RequirePermission(PermissionCodes.Applications.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<LoginDesignSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetDesigns(Guid applicationId, CancellationToken cancellationToken) =>
        List(applicationId, cancellationToken);

    /// <summary>One design in full, for its editor.</summary>
    [HttpGet("{designId:guid}")]
    [RequirePermission(PermissionCodes.Applications.Read)]
    [ProducesResponseType(typeof(LoginDesignDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetDesign(Guid applicationId, Guid designId, CancellationToken cancellationToken) =>
        Get(applicationId, designId, cancellationToken);

    /// <summary>Creates a design. It starts inactive.</summary>
    [HttpPost]
    [RequirePermission(PermissionCodes.Applications.Update)]
    [ProducesResponseType(typeof(LoginDesignDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> CreateDesign(
        Guid applicationId,
        [FromBody] CreateLoginDesignRequest request,
        CancellationToken cancellationToken) =>
        Create(applicationId, request, nameof(GetDesign), cancellationToken);

    /// <summary>Saves a design; on the active design the save is live.</summary>
    [HttpPut("{designId:guid}")]
    [RequirePermission(PermissionCodes.Applications.Update)]
    [ProducesResponseType(typeof(LoginDesignDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> UpdateDesign(
        Guid applicationId,
        Guid designId,
        [FromBody] UpdateLoginDesignRequest request,
        CancellationToken cancellationToken) =>
        Update(applicationId, designId, request, cancellationToken);

    /// <summary>Copies a design into a new, inactive one.</summary>
    [HttpPost("{designId:guid}/copy")]
    [RequirePermission(PermissionCodes.Applications.Update)]
    [ProducesResponseType(typeof(LoginDesignDto), StatusCodes.Status201Created)]
    public Task<IActionResult> CopyDesign(
        Guid applicationId,
        Guid designId,
        [FromBody] CopyLoginDesignRequest request,
        CancellationToken cancellationToken) =>
        Copy(applicationId, designId, request, nameof(GetDesign), cancellationToken);

    /// <summary>Makes a design the one the application's sign-in pages show.</summary>
    [HttpPost("{designId:guid}/activate")]
    [RequirePermission(PermissionCodes.Applications.Update)]
    [ProducesResponseType(typeof(LoginDesignDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ActivateDesign(Guid applicationId, Guid designId, CancellationToken cancellationToken) =>
        Activate(applicationId, designId, cancellationToken);

    /// <summary>Stops showing a design: the pages fall back to the platform's design.</summary>
    [HttpPost("{designId:guid}/deactivate")]
    [RequirePermission(PermissionCodes.Applications.Update)]
    [ProducesResponseType(typeof(LoginDesignDto), StatusCodes.Status200OK)]
    public Task<IActionResult> DeactivateDesign(Guid applicationId, Guid designId, CancellationToken cancellationToken) =>
        Deactivate(applicationId, designId, cancellationToken);

    /// <summary>Deletes an inactive design; the active one is refused (409).</summary>
    [HttpDelete("{designId:guid}")]
    [RequirePermission(PermissionCodes.Applications.Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> DeleteDesign(Guid applicationId, Guid designId, CancellationToken cancellationToken) =>
        Delete(applicationId, designId, cancellationToken);

    /// <summary>Renders a design as the editor holds it, without saving it.</summary>
    [HttpPost("preview")]
    [RequirePermission(PermissionCodes.Applications.Read)]
    [ProducesResponseType(typeof(LoginDesignPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> PreviewDesign(
        Guid applicationId,
        [FromBody] PreviewLoginDesignRequest request,
        CancellationToken cancellationToken) =>
        Preview(applicationId, request, cancellationToken);
}
