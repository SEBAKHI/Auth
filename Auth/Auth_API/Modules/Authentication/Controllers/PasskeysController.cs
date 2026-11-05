using System.Text.Json;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.SendTwoFactorEmailCode;
using Auth.Application.Features.Passkeys.BeginPasskeyRegistration;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Features.Passkeys.CompletePasskeyRegistration;
using Auth.Application.Features.Passkeys.GetMyPasskeys;
using Auth.Application.Features.Passkeys.RemovePasskey;
using Auth.Application.Features.Passkeys.RenamePasskey;
using Auth.Application.Features.Passkeys.SendPasskeyRegistrationEmailCode;
using Auth_API.Common;
using Auth_API.Common.Passkeys;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.Controllers;

/// <summary>
/// The signed-in account's passkeys (S20): add one, list them, rename and remove one. Signing
/// in with a passkey is not here yet (S20 PR B).
/// </summary>
/// <remarks>
/// <para>
/// Every action answers 404 while passkeys do not run — switched off, or switched on without
/// an RP ID, a first-party origin or the schema (PK19, <see cref="PasskeysAvailabilityMiddleware"/>).
/// </para>
/// <para>
/// The route is a lower-case literal: a cookie's path and the gateway's routes are
/// case-sensitive. The ceremony cookie is <c>__Host-</c> and Strict (<see cref="WebAuthnChallengeCookie"/>).
/// </para>
/// <para>
/// No answer here may be stored by a browser or a proxy.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth/passkeys")]
[Produces("application/json")]
[Authorize]
[PasskeysEndpoint]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PasskeysController : ApiController
{
    /// <summary>The largest registration answer accepted: an RSA key's attestation fits many times over.</summary>
    public const int MaxRegistrationBodyBytes = 64 * 1024;

    private readonly ISender _sender;
    private readonly IdentityProviderSettings _idpSettings;

    public PasskeysController(ISender sender, IOptionsSnapshot<IdentityProviderSettings> idpSettings)
    {
        _sender = sender;
        _idpSettings = idpSettings.Value;
    }

    /// <summary>
    /// Starts adding a passkey: the options for <c>navigator.credentials.create()</c>. The
    /// challenge they carry is held for five minutes; its handle travels in the
    /// <c>__Host-auth_webauthn</c> cookie, never in the body.
    /// </summary>
    /// <remarks>
    /// Needs a recent sign-in that proved a first factor, and two factors when the account
    /// already has a qualifying one (<c>Auth.ReauthenticationRequired</c>). <c>emailCodeRequired</c>
    /// says whether completing it needs the code emailed to the account (its first factor).
    /// </remarks>
    [HttpPost("registration/options")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(PasskeyRegistrationOptionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> BeginRegistration(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new BeginPasskeyRegistrationCommand(userId, GetCurrentSessionId()), cancellationToken);
        if (result.IsError)
        {
            return Problem(result.Errors);
        }

        WebAuthnChallengeCookie.Apply(Response, result.Value.ChallengeHandle);
        using var options = JsonDocument.Parse(result.Value.OptionsJson);
        return Ok(new PasskeyRegistrationOptionsResponse(
            options.RootElement.Clone(), result.Value.ExpiresAt, result.Value.EmailCodeRequired));
    }

    /// <summary>
    /// Emails the code an account enters before it binds a passkey as its first qualifying
    /// second factor, to its confirmed address. A fresh code supersedes any earlier one.
    /// </summary>
    /// <remarks>
    /// Answers <c>emailCodeRequired: false</c>, and sends nothing, while no code is needed:
    /// email off, or a qualifying factor already. <c>TwoFactor.SetupRequired</c> when no
    /// registration was started. The code is never returned.
    /// </remarks>
    [HttpPost("registration/email-code")]
    [EnableRateLimiting("two-factor-email-code")]
    [ProducesResponseType(typeof(TwoFactorEmailCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendRegistrationEmailCode(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new SendPasskeyRegistrationEmailCodeCommand(userId, GetCurrentSessionId(), GetClientIpAddress()),
            cancellationToken);

        return result.Match<IActionResult>(response => Ok(response), errors => Problem(errors));
    }

    /// <summary>
    /// Completes adding a passkey: the browser's <c>PublicKeyCredential</c>, checked against
    /// the challenge the cookie names. The challenge is spent whatever the outcome, once the
    /// emailed code (when one is needed) was accepted.
    /// </summary>
    /// <remarks>
    /// Every rejection of the ceremony is <c>Passkey.VerificationFailed</c>; the eleventh live
    /// passkey <c>Passkey.LimitReached</c>; a name over 64 characters <c>Passkey.NameTooLong</c>.
    /// </remarks>
    [HttpPost("registration/verify")]
    [EnableRateLimiting("login")]
    [RequestSizeLimit(MaxRegistrationBodyBytes)]
    [ProducesResponseType(typeof(PasskeyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CompleteRegistration(
        [FromBody] CompletePasskeyRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var (handle, repeated) = WebAuthnChallengeCookie.Read(Request);

        // A body without a credential object is checked like any malformed answer: it fails
        // verification, rather than the request failing on reading it.
        var credentialJson = request.Credential.ValueKind == JsonValueKind.Object
            ? request.Credential.GetRawText()
            : string.Empty;

        var result = await _sender.Send(
            new CompletePasskeyRegistrationCommand(
                userId,
                GetCurrentSessionId(),
                handle,
                repeated,
                credentialJson,
                request.Name,
                request.EmailCode),
            cancellationToken);

        if (result.IsError)
        {
            return Problem(result.Errors);
        }

        WebAuthnChallengeCookie.Delete(Response);
        return Ok(result.Value);
    }

    /// <summary>The account's live passkeys, oldest first.</summary>
    [HttpGet]
    [EnableRateLimiting("sign-in-page")]
    [ProducesResponseType(typeof(IReadOnlyList<PasskeyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new GetMyPasskeysQuery(userId), cancellationToken);
        return result.Match<IActionResult>(passkeys => Ok(passkeys), errors => Problem(errors));
    }

    /// <summary>Gives one of the account's passkeys another name.</summary>
    [HttpPatch("{id:guid}")]
    [EnableRateLimiting("sign-in-page")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenamePasskeyRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new RenamePasskeyCommand(userId, id, request.Name), cancellationToken);
        return result.Match<IActionResult>(_ => NoContent(), errors => Problem(errors));
    }

    /// <summary>
    /// Removes one of the account's passkeys, and signs out every other session of the
    /// account; this one stays.
    /// </summary>
    /// <remarks>
    /// Needs a recent sign-in that proved a first factor, and two factors when the account has
    /// the authenticator app. The last qualifying factor of an administrator the two-factor
    /// policy binds stays (<c>TwoFactor.RequiredByPolicy</c>).
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new RemovePasskeyCommand(userId, id, GetCurrentSessionId(), IdpSessionCookie.Read(Request, _idpSettings)),
            cancellationToken);
        return result.Match<IActionResult>(_ => NoContent(), errors => Problem(errors));
    }
}

/// <summary>The options of a passkey registration.</summary>
/// <param name="Options">The options for <c>navigator.credentials.create()</c>, as JSON (WebAuthn L3 PublicKeyCredentialCreationOptionsJSON).</param>
/// <param name="ExpiresAt">When the challenge stops being accepted (UTC).</param>
/// <param name="EmailCodeRequired">Whether completing it needs the code emailed to the account.</param>
public sealed record PasskeyRegistrationOptionsResponse(JsonElement Options, DateTime ExpiresAt, bool EmailCodeRequired);

/// <summary>The answer to a passkey registration.</summary>
/// <param name="Credential">The browser's <c>PublicKeyCredential</c>, as JSON (RegistrationResponseJSON).</param>
/// <param name="Name">The name the owner gives the passkey; blank takes a default.</param>
/// <param name="EmailCode">The code emailed for a first qualifying factor, when one is needed.</param>
public sealed record CompletePasskeyRegistrationRequest(JsonElement Credential, string? Name, string? EmailCode)
{
    // The code is a secret while it lives.
    public override string ToString() => $"CompletePasskeyRegistrationRequest {{ Name = {Name} }}";
}

/// <summary>A passkey's new name.</summary>
/// <param name="Name">The name; blank takes a default.</param>
public sealed record RenamePasskeyRequest(string? Name);
