using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.NotificationManagement.Contracts;
using Auth.Application.DTOs;
using Auth.Application.Features.PrivacyPolicy.CreatePrivacyPolicyVersion;
using Auth.Application.Features.PrivacyPolicy.GetPrivacyPolicyContent;
using Auth.Application.Features.PrivacyPolicy.GetPrivacyPolicyVersions;
using Auth.Application.Features.PrivacyPolicy.GetPublishedPrivacyPolicy;
using Auth.Application.Features.PrivacyPolicy.NotifyPrivacyPolicyVersion;
using Auth.Application.Features.PrivacyPolicy.PublishPrivacyPolicyVersion;
using Auth.Application.Features.PrivacyPolicy.SavePrivacyPolicyContent;
using Auth.Application.Features.PrivacyPolicy.UpdatePrivacyPolicyVersion;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.NotificationManagement.Controllers;

/// <summary>
/// The privacy policy: its published content (per language), its revision
/// registry, and the change-notification run. Policy text lives in the
/// database, not in source, so legal wording is edited here rather than
/// deployed; the numeric disclosures it quotes are injected from the running
/// configuration at read time, so the text can never contradict the system.
///
/// Guarded by its own <c>privacy-policy:*</c> claims — publishing legal text
/// is a distinct duty from operating the notification system.
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
    /// Gets the published policy for a language (falling back to English) plus
    /// the live configuration disclosures. Anonymous: this is the public
    /// compliance surface the accounts app renders.
    /// </summary>
    [HttpGet("published")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublishedPrivacyPolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublished(
        [FromQuery] string? language, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetPublishedPrivacyPolicyQuery(language), cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Gets every recorded policy revision, newest first.
    /// </summary>
    [HttpGet("versions")]
    [RequirePermission("privacy-policy:read")]
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
    [RequirePermission("privacy-policy:manage")]
    [ProducesResponseType(typeof(PrivacyPolicyVersionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateVersion(
        [FromBody] CreatePrivacyPolicyVersionRequest request, CancellationToken cancellationToken)
    {
        var command = new CreatePrivacyPolicyVersionCommand(
            request.Version, request.EffectiveDateUtc, request.ChangeNote)
        {
            RequestedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(
            dto => CreatedAtAction(nameof(GetVersions), null, dto),
            Problem);
    }

    /// <summary>
    /// Updates a revision's effective date and change note.
    /// </summary>
    [HttpPut("versions")]
    [RequirePermission("privacy-policy:manage")]
    [ProducesResponseType(typeof(PrivacyPolicyVersionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateVersion(
        [FromBody] UpdatePrivacyPolicyVersionRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdatePrivacyPolicyVersionCommand(
            request.Version, request.EffectiveDateUtc, request.ChangeNote)
        {
            RequestedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Gets one language document of a revision for editing (drafts included).
    /// </summary>
    [HttpGet("versions/content")]
    [RequirePermission("privacy-policy:read")]
    [ProducesResponseType(typeof(PrivacyPolicyContentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetContent(
        [FromQuery] string version,
        [FromQuery] string language,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetPrivacyPolicyContentQuery(version, language), cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Creates or replaces one language document of a revision. The payload is
    /// validated as a well-formed policy document before storage.
    /// </summary>
    [HttpPut("versions/content")]
    [RequirePermission("privacy-policy:manage")]
    [ProducesResponseType(typeof(PrivacyPolicyContentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveContent(
        [FromBody] SavePrivacyPolicyContentRequest request, CancellationToken cancellationToken)
    {
        var command = new SavePrivacyPolicyContentCommand(
            request.Version, request.LanguageCode, request.ContentJson)
        {
            RequestedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Makes a revision the published policy served to end users.
    /// </summary>
    [HttpPost("versions/publish")]
    [RequirePermission("privacy-policy:manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PublishVersion(
        [FromBody] PrivacyPolicyVersionRequest request, CancellationToken cancellationToken)
    {
        var command = new PublishPrivacyPolicyVersionCommand(request.Version)
        {
            RequestedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(_ => NoContent(), Problem);
    }

    /// <summary>
    /// Sends the policy-change notice for a recorded revision to every
    /// active, email-confirmed user, each in their preferred language, and
    /// stamps the revision with the delivery time and count.
    /// </summary>
    [HttpPost("versions/notify")]
    [RequirePermission("privacy-policy:manage")]
    [ProducesResponseType(typeof(PrivacyPolicyNotifyResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> NotifyVersion(
        [FromBody] PrivacyPolicyVersionRequest request, CancellationToken cancellationToken)
    {
        var command = new NotifyPrivacyPolicyVersionCommand(request.Version)
        {
            RequestedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }
}
