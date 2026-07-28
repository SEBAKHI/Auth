using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.NotificationManagement.Contracts;
using Auth.Application.DTOs;
using Auth.Application.Features.PrivacyPolicy.CreatePrivacyPolicyVersion;
using Auth.Application.Features.PrivacyPolicy.GetPrivacyPolicyVersions;
using Auth.Application.Features.PrivacyPolicy.NotifyPrivacyPolicyVersion;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.NotificationManagement.Controllers;

/// <summary>
/// The privacy-policy revision registry: which policy versions exist, when
/// each took effect, and when (and to how many users) the change notice was
/// sent — the auditable record behind the policy's own "we notify you of
/// material changes" promise. Reuses the notification-management claims: the
/// notice is a notification-side concern, sent from the seeded
/// privacy-policy-updated template.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/privacy-policy")]
[Authorize]
public class PrivacyPolicyController : ApiController
{
    private readonly ISender _sender;

    public PrivacyPolicyController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Gets every recorded policy revision, newest first.
    /// </summary>
    [HttpGet("versions")]
    [RequirePermission("notification-templates:read")]
    [ProducesResponseType(typeof(IReadOnlyList<PrivacyPolicyVersionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVersions(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetPrivacyPolicyVersionsQuery(), cancellationToken);
        return result.Match(versions => Ok(versions), Problem);
    }

    /// <summary>
    /// Records a new policy revision ("YYYY.MM").
    /// </summary>
    [HttpPost("versions")]
    [RequirePermission("notification-templates:manage")]
    [ProducesResponseType(typeof(PrivacyPolicyVersionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateVersion(
        [FromBody] CreatePrivacyPolicyVersionRequest request, CancellationToken cancellationToken)
    {
        var command = new CreatePrivacyPolicyVersionCommand(request.Version, request.EffectiveDateUtc)
        {
            RequestedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(
            dto => CreatedAtAction(nameof(GetVersions), null, dto),
            Problem);
    }

    /// <summary>
    /// Sends the policy-change notice for a recorded revision to every
    /// active, email-confirmed user, each in their preferred language, and
    /// stamps the revision with the delivery time and count.
    /// </summary>
    [HttpPost("versions/notify")]
    [RequirePermission("notification-templates:manage")]
    [ProducesResponseType(typeof(PrivacyPolicyNotifyResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> NotifyVersion(
        [FromBody] NotifyPrivacyPolicyVersionRequest request, CancellationToken cancellationToken)
    {
        var command = new NotifyPrivacyPolicyVersionCommand(request.Version)
        {
            RequestedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }
}
