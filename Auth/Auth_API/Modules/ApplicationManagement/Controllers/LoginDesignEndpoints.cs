using Auth_API.Common;
using Auth_API.Modules.ApplicationManagement.Contracts;
using Auth.Application.Features.LoginDesigns.ActivateLoginDesign;
using Auth.Application.Features.LoginDesigns.CopyLoginDesign;
using Auth.Application.Features.LoginDesigns.CreateLoginDesign;
using Auth.Application.Features.LoginDesigns.DeactivateLoginDesign;
using Auth.Application.Features.LoginDesigns.DeleteLoginDesign;
using Auth.Application.Features.LoginDesigns.GetLoginDesignById;
using Auth.Application.Features.LoginDesigns.GetLoginDesigns;
using Auth.Application.Features.LoginDesigns.PreviewLoginDesign;
using Auth.Application.Features.LoginDesigns.UpdateLoginDesign;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.ApplicationManagement.Controllers;

/// <summary>
/// The sign-in page design endpoints, written once for both scopes: an application's designs
/// (<see cref="ApplicationLoginDesignsController"/>) and the platform's
/// (<see cref="PlatformLoginDesignsController"/>). Each controller declares its routes and
/// permissions and passes its scope; a design of another scope answers 404.
/// </summary>
public abstract class LoginDesignEndpoints : ApiController
{
    private readonly ISender _sender;

    protected LoginDesignEndpoints(ISender sender)
    {
        _sender = sender;
    }

    protected async Task<IActionResult> List(Guid? applicationId, CancellationToken cancellationToken) =>
        (await _sender.Send(new GetLoginDesignsQuery(applicationId), cancellationToken))
            .Match(dto => Ok(dto), Problem);

    protected async Task<IActionResult> Get(Guid? applicationId, Guid designId, CancellationToken cancellationToken) =>
        (await _sender.Send(new GetLoginDesignByIdQuery(applicationId, designId), cancellationToken))
            .Match(dto => Ok(dto), Problem);

    protected async Task<IActionResult> Create(
        Guid? applicationId,
        CreateLoginDesignRequest request,
        string getActionName,
        CancellationToken cancellationToken)
    {
        var command = new CreateLoginDesignCommand(
            applicationId, request.Name, request.Content, request.StringsJson, request.ShowPlatformLogo)
        {
            CreatedBy = GetCurrentUserId()
        };

        return (await _sender.Send(command, cancellationToken)).Match(
            dto => CreatedAtAction(getActionName, RouteOf(applicationId, dto.Id), dto),
            Problem);
    }

    protected async Task<IActionResult> Update(
        Guid? applicationId,
        Guid designId,
        UpdateLoginDesignRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateLoginDesignCommand(
            applicationId, designId, request.Name, request.Content, request.StringsJson,
            request.ShowPlatformLogo, request.ExpectedRevision)
        {
            ModifiedBy = GetCurrentUserId()
        };

        return (await _sender.Send(command, cancellationToken)).Match(dto => Ok(dto), Problem);
    }

    protected async Task<IActionResult> Copy(
        Guid? applicationId,
        Guid designId,
        CopyLoginDesignRequest request,
        string getActionName,
        CancellationToken cancellationToken)
    {
        var command = new CopyLoginDesignCommand(applicationId, designId, request.Name)
        {
            CreatedBy = GetCurrentUserId()
        };

        return (await _sender.Send(command, cancellationToken)).Match(
            dto => CreatedAtAction(getActionName, RouteOf(applicationId, dto.Id), dto),
            Problem);
    }

    protected async Task<IActionResult> Activate(Guid? applicationId, Guid designId, CancellationToken cancellationToken) =>
        (await _sender.Send(
            new ActivateLoginDesignCommand(applicationId, designId) { ActivatedBy = GetCurrentUserId() },
            cancellationToken))
        .Match(dto => Ok(dto), Problem);

    protected async Task<IActionResult> Deactivate(Guid? applicationId, Guid designId, CancellationToken cancellationToken) =>
        (await _sender.Send(
            new DeactivateLoginDesignCommand(applicationId, designId) { DeactivatedBy = GetCurrentUserId() },
            cancellationToken))
        .Match(dto => Ok(dto), Problem);

    protected async Task<IActionResult> Delete(Guid? applicationId, Guid designId, CancellationToken cancellationToken) =>
        (await _sender.Send(
            new DeleteLoginDesignCommand(applicationId, designId) { DeletedBy = GetCurrentUserId() },
            cancellationToken))
        .Match(_ => NoContent(), Problem);

    protected async Task<IActionResult> Preview(
        Guid? applicationId,
        PreviewLoginDesignRequest request,
        CancellationToken cancellationToken) =>
        (await _sender.Send(
            new PreviewLoginDesignQuery(applicationId, request.Content, request.StringsJson, request.Language),
            cancellationToken))
        .Match(dto => Ok(dto), Problem);

    private static object RouteOf(Guid? applicationId, Guid designId) => applicationId is { } id
        ? new { applicationId = id, designId, version = "1" }
        : new { designId, version = "1" };
}
